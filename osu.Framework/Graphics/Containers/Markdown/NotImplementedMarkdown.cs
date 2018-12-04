﻿// Copyright (c) 2007-2018 ppy Pty Ltd <contact@ppy.sh>.
// Licensed under the MIT Licence - https://raw.githubusercontent.com/ppy/osu-framework/master/LICENCE

using Markdig.Syntax;
using osu.Framework.Allocation;
using osu.Framework.Graphics.Sprites;
using osuTK.Graphics;

namespace osu.Framework.Graphics.Containers.Markdown
{
    /// <summary>
    /// Visualises a message that displays when a <see cref="IMarkdownObject"/> doesn't have a visual implementation.
    /// </summary>
    public class NotImplementedMarkdown : CompositeDrawable, IMarkdownTextComponent
    {
        private readonly IMarkdownObject markdownObject;

        [Resolved]
        private IMarkdownTextComponent parentTextComponent { get; set; }

        public NotImplementedMarkdown(IMarkdownObject markdownObject)
        {
            this.markdownObject = markdownObject;

            AutoSizeAxes = Axes.Y;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            InternalChild = CreateText();
        }

        public SpriteText CreateText()
        {
            var text = parentTextComponent.CreateText();
            text.Colour = new Color4(255, 0, 0, 255);
            text.TextSize = 21;
            text.Text = markdownObject?.GetType() + " Not implemented.";
            return text;
        }
    }
}
