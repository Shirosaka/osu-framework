// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.IO;

namespace osu.Framework.Platform
{
    /// <summary>
    /// A <see cref="FileStream"/> which always flushes to disk on disposal.
    /// </summary>
    /// <remarks>
    /// This adds a considerable overhead, but is required to avoid files being potentially written to disk in a corrupt state.
    /// See https://stackoverflow.com/questions/49260358/what-could-cause-an-xml-file-to-be-filled-with-null-characters/52751216#52751216.
    /// </remarks>
    public class FlushingStream : FileStream
    {
        public FlushingStream(string path, FileMode mode, FileAccess access)
            : base(path, mode, access)
        {
        }

        protected override void Dispose(bool disposing)
        {
            try
            {
                Flush(true);
            }
            catch
            {
                // on some platforms, may fail due to the stream already being closed, or never being opened.
                // we don't really care about such failures.
            }

            base.Dispose(disposing);
        }
    }
}
