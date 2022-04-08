// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

namespace osu.Framework.Screens
{
    public class ScreenResumeEvent : ScreenEvent
    {
        /// <summary>
        /// The <see cref="IScreen"/> which has exited.
        /// </summary>
        public IScreen Last;

        public ScreenResumeEvent(IScreen last)
        {
            Last = last;
        }
    }
}
