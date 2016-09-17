﻿//Copyright (c) 2007-2016 ppy Pty Ltd <contact@ppy.sh>.
//Licensed under the MIT Licence - https://raw.githubusercontent.com/ppy/osu-framework/master/LICENCE

using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using OpenTK.Graphics.ES20;
using PixelFormat = OpenTK.Graphics.ES20.PixelFormat;
using System.Diagnostics;
using OpenTK.Graphics;
using OpenTK;
using osu.Framework.Graphics.Batches;
using osu.Framework.Graphics.Primitives;
using System.Drawing;
using osu.Framework.DebugUtils;
using RectangleF = osu.Framework.Graphics.Primitives.RectangleF;

namespace osu.Framework.Graphics.OpenGL.Textures
{
    class TextureGLSingle : TextureGL
    {
        private static VertexBatch<TexturedVertex2d> spriteBatch;

        private ConcurrentQueue<TextureUpload> uploadQueue = new ConcurrentQueue<TextureUpload>();

        private int internalWidth;
        private int internalHeight;

        private TextureWrapMode internalWrapMode;

        public override bool Loaded => textureId > 0 || uploadQueue.Count > 0;

        public TextureGLSingle(int width, int height)
        {
            Width = width;
            Height = height;
        }

        #region Disposal
        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);
            unload();
        }

        /// <summary>
        /// Removes texture from GL memory.
        /// </summary>
        private void unload()
        {
            TextureUpload u;
            while (uploadQueue.TryDequeue(out u))
                u.Dispose();

            int disposableId = textureId;

            if (disposableId <= 0)
                return;

            GLWrapper.DeleteTextures(disposableId);

            textureId = 0;
        }
        #endregion

        private int height;
        public override int Height
        {
            get
            {
                Debug.Assert(!isDisposed);
                return height;
            }

            set
            {
                Debug.Assert(!isDisposed);
                height = value;
            }
        }

        private int width;
        public override int Width
        {
            get
            {
                Debug.Assert(!isDisposed);
                return width;
            }

            set
            {
                Debug.Assert(!isDisposed);
                width = value;
            }
        }

        private int textureId;
        public override int TextureId
        {
            get
            {
                Debug.Assert(!isDisposed);

                if (uploadQueue.Count > 0)
                    Upload();

                return textureId;
            }
        }

        private static void RotateVector(ref Vector2 toRotate, float sin, float cos)
        {
            float oldX = toRotate.X;
            toRotate.X = toRotate.X * cos - toRotate.Y * sin;
            toRotate.Y = oldX * sin + toRotate.Y * cos;
        }

        /// <summary>
        /// Blits sprite to OpenGL display with specified parameters.
        /// </summary>
        public override void Draw(Quad vertexQuad, RectangleF? textureRect, Color4 drawColour, VertexBatch<TexturedVertex2d> spriteBatch = null)
        {
            Debug.Assert(!isDisposed);

            if (!Bind())
                return;

            RectangleF texRect = textureRect != null ?
                new RectangleF(textureRect.Value.X, textureRect.Value.Y, textureRect.Value.Width, textureRect.Value.Height) :
                new RectangleF(0, 0, Width, Height);

            texRect.X /= width;
            texRect.Y /= height;
            texRect.Width /= width;
            texRect.Height /= height;

            if (spriteBatch == null)
            {
                if (TextureGLSingle.spriteBatch == null)
                    TextureGLSingle.spriteBatch = new QuadBatch<TexturedVertex2d>(1, 100);
                spriteBatch = TextureGLSingle.spriteBatch;
            }

            spriteBatch.Add(new TexturedVertex2d() { Position = vertexQuad.BottomLeft, TexturePosition = new Vector2(texRect.Left, texRect.Bottom), Colour = drawColour });
            spriteBatch.Add(new TexturedVertex2d() { Position = vertexQuad.BottomRight, TexturePosition = new Vector2(texRect.Right, texRect.Bottom), Colour = drawColour });
            spriteBatch.Add(new TexturedVertex2d() { Position = vertexQuad.TopRight, TexturePosition = new Vector2(texRect.Right, texRect.Top), Colour = drawColour });
            spriteBatch.Add(new TexturedVertex2d() { Position = vertexQuad.TopLeft, TexturePosition = new Vector2(texRect.Left, texRect.Top), Colour = drawColour });
        }

        private void updateWrapMode()
        {
            Debug.Assert(!isDisposed);

            internalWrapMode = WrapMode;
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)internalWrapMode);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)internalWrapMode);
        }

        public override void SetData(TextureUpload upload)
        {
            Debug.Assert(!isDisposed);

            if (upload.Bounds == Rectangle.Empty)
                upload.Bounds = new Rectangle(0, 0, width, height);

            IsTransparent = false;
            uploadQueue.Enqueue(upload);
            GLWrapper.EnqueueTextureUpload(this);
        }

        public override bool Bind()
        {
            Debug.Assert(!isDisposed);

            Upload();

            if (textureId <= 0)
                return false;

            if (IsTransparent)
                return false;

            GLWrapper.BindTexture(textureId);

            if (internalWrapMode != WrapMode)
                updateWrapMode();

            return true;
        }

        /// <summary>
        /// This is used for initializing power-of-two sized textures to transparent to avoid artifacts.
        /// </summary>
        private static byte[] transparentWhite = new byte[GLWrapper.MaxTextureSize * GLWrapper.MaxTextureSize * 4];

        static TextureGLSingle()
        {
            fixAlpha(transparentWhite);
        }

        bool manualMipmaps;

        private static unsafe void fixAlpha(byte[] data)
        {
            fixed (byte* dPtr = &data[0])
            {
                byte* sp = dPtr;
                byte* ep = dPtr + data.Length;

                while (sp < ep)
                {
                    *(sp + 0) = 255;
                    *(sp + 1) = 255;
                    *(sp + 2) = 255;
                    sp += 4;
                }
            }
        }

        internal override bool Upload()
        {
            // We should never run raw OGL calls on another thread than the main thread due to race conditions.
            ThreadSafety.EnsureDrawThread();

            if (isDisposed)
                return false;

            IntPtr dataPointer;
            GCHandle? h0;
            TextureUpload upload;
            bool didUpload = false;

            while (uploadQueue.TryDequeue(out upload))
            {
                if (upload.Data.Length == 0)
                {
                    h0 = null;
                    dataPointer = IntPtr.Zero;
                }
                else
                {
                    h0 = GCHandle.Alloc(upload.Data, GCHandleType.Pinned);
                    dataPointer = h0.Value.AddrOfPinnedObject();
                    didUpload = true;
                }

                try
                {
                    // Do we need to generate a new texture?
                    if (textureId <= 0 || internalWidth < width || internalHeight < height)
                    {
                        internalWidth = width;
                        internalHeight = height;

                        // We only need to generate a new texture if we don't have one already. Otherwise just re-use the current one.
                        if (textureId <= 0)
                        {
                            int[] textures = new int[1];
                            GL.GenTextures(1, textures);

                            textureId = textures[0];

                            GLWrapper.BindTexture(textureId);
                            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)All.LinearMipmapLinear);
                            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)All.Linear);

                            updateWrapMode();
                        }
                        else
                            GLWrapper.BindTexture(textureId);

                        if (width == upload.Bounds.Width && height == upload.Bounds.Height || dataPointer == IntPtr.Zero)
                            GL.TexImage2D(TextureTarget2d.Texture2D, upload.Level, TextureComponentCount.Rgba, width, height, 0, upload.Format, PixelType.UnsignedByte, dataPointer);
                        else
                        {
                            GCHandle h1 = GCHandle.Alloc(transparentWhite, GCHandleType.Pinned);
                            GL.TexImage2D(TextureTarget2d.Texture2D, upload.Level, TextureComponentCount.Rgba, width, height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, h1.AddrOfPinnedObject());
                            h1.Free();

                            GL.TexSubImage2D(TextureTarget2d.Texture2D, upload.Level, upload.Bounds.X, upload.Bounds.Y, upload.Bounds.Width, upload.Bounds.Height, upload.Format, PixelType.UnsignedByte, dataPointer);
                        }
                    }
                    // Just update content of the current texture
                    else if (dataPointer != IntPtr.Zero)
                    {
                        GLWrapper.BindTexture(textureId);

                        if (!manualMipmaps && upload.Level > 0)
                        {
                            GCHandle h1 = GCHandle.Alloc(transparentWhite, GCHandleType.Pinned);

                            //allocate mipmap levels
                            int level = 1;
                            int d = 2;

                            while (width / d > 0)
                            {
                                GL.TexImage2D(TextureTarget2d.Texture2D, level, TextureComponentCount.Rgba, width / d, height / d, 0, PixelFormat.Rgba, PixelType.UnsignedByte, h1.AddrOfPinnedObject());
                                level++;
                                d *= 2;
                            }

                            h1.Free();

                            manualMipmaps = true;
                        }

                        int div = (int)Math.Pow(2, upload.Level);

                        GL.TexSubImage2D(TextureTarget2d.Texture2D, upload.Level, upload.Bounds.X / div, upload.Bounds.Y / div, upload.Bounds.Width / div, upload.Bounds.Height / div, upload.Format, PixelType.UnsignedByte, dataPointer);
                    }
                }
                finally
                {
                    h0?.Free();
                    upload?.Dispose();
                }
            }

            if (didUpload && !manualMipmaps)
            {
                GL.Hint(HintTarget.GenerateMipmapHint, HintMode.Nicest);
                GL.GenerateMipmap(TextureTarget.Texture2D);
            }

            return didUpload;
        }
    }
}
