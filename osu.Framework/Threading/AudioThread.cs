﻿// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Statistics;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ManagedBass;
using osu.Framework.Audio;
using osu.Framework.Development;
using osu.Framework.Platform.Linux.Native;

namespace osu.Framework.Threading
{
    public class AudioThread : GameThread
    {
        public AudioThread()
            : base(name: "Audio")
        {
            OnNewFrame += onNewFrame;
            PreloadBass();
        }

        public override bool IsCurrent => ThreadSafety.IsAudioThread;

        internal sealed override void MakeCurrent()
        {
            base.MakeCurrent();

            ThreadSafety.IsAudioThread = true;
        }

        internal override IEnumerable<StatisticsCounterType> StatisticsCounters => new[]
        {
            StatisticsCounterType.TasksRun,
            StatisticsCounterType.Tracks,
            StatisticsCounterType.Samples,
            StatisticsCounterType.SChannels,
            StatisticsCounterType.Components,
            StatisticsCounterType.MixChannels,
        };

        private readonly List<AudioManager> managers = new List<AudioManager>();

        private static readonly HashSet<int> initialised_devices = new HashSet<int>();

        private static readonly GlobalStatistic<double> cpu_usage = GlobalStatistics.Get<double>("Audio", "Bass CPU%");

        private void onNewFrame()
        {
            cpu_usage.Value = Bass.CPUUsage;

            lock (managers)
            {
                for (var i = 0; i < managers.Count; i++)
                {
                    var m = managers[i];
                    m.Update();
                }
            }
        }

        internal void RegisterManager(AudioManager manager)
        {
            lock (managers)
            {
                if (managers.Contains(manager))
                    throw new InvalidOperationException($"{manager} was already registered");

                managers.Add(manager);
            }
        }

        internal void UnregisterManager(AudioManager manager)
        {
            lock (managers)
                managers.Remove(manager);
        }

        internal void RegisterInitialisedDevice(int deviceId)
        {
            Debug.Assert(ThreadSafety.IsAudioThread);
        }

        protected override void OnExit()
        {
            base.OnExit();

            lock (managers)
            {
                // AudioManagers are iterated over backwards since disposal will unregister and remove them from the list.
                for (int i = managers.Count - 1; i >= 0; i--)
                {
                    var m = managers[i];

                    m.Dispose();

                    // Audio component disposal (including the AudioManager itself) is scheduled and only runs when the AudioThread updates.
                    // But the AudioThread won't run another update since it's exiting, so an update must be performed manually in order to finish the disposal.
                    m.Update();
                }

                managers.Clear();
            }

            // Safety net to ensure we have freed all devices before exiting.
            // This is mainly required for device-lost scenarios.
            // See https://github.com/ppy/osu-framework/pull/3378 for further discussion.
            foreach (var d in initialised_devices.ToArray())
                FreeDevice(d);
        }

        internal static bool InitDevice(int deviceId)
        {
            Debug.Assert(ThreadSafety.IsAudioThread);

            bool didInit = Bass.Init(deviceId);

            // If the device was already initialised, the device can be used without much fuss.
            if (Bass.LastError == Errors.Already)
            {
                Bass.CurrentDevice = deviceId;

                if (Bass.CurrentDevice == 0 && RuntimeInfo.OS == RuntimeInfo.Platform.Linux)
                {
                    // Device 0 is never freed on linux (see: FreeDevice()).
                    didInit = true;
                }
                else
                {
                    // Without this call, on windows (and potentially other platforms), a device which is disconnected then reconnected
                    // will look initialised but not work correctly in practice.
                    FreeDevice(deviceId);
                    didInit = Bass.Init(deviceId);
                }
            }

            if (didInit)
                initialised_devices.Add(deviceId);

            return didInit;
        }

        internal static void FreeDevice(int deviceId)
        {
            Console.WriteLine($"Freeing device {deviceId}");

            Debug.Assert(ThreadSafety.IsAudioThread);

            int lastDevice = Bass.CurrentDevice;

            // Freeing device 0 on linux can cause deadlocks. This doesn't always happen immediately.
            // Todo: Reproduce in native code and report to BASS at some point.
            if (deviceId != 0 || RuntimeInfo.OS != RuntimeInfo.Platform.Linux)
            {
                Bass.CurrentDevice = deviceId;
                Bass.Free();
            }

            if (lastDevice != deviceId)
                Bass.CurrentDevice = lastDevice;

            initialised_devices.Remove(deviceId);
        }

        /// <summary>
        /// Makes BASS available to be consumed.
        /// </summary>
        internal static void PreloadBass()
        {
            if (RuntimeInfo.OS == RuntimeInfo.Platform.Linux)
            {
                // required for the time being to address libbass_fx.so load failures (see https://github.com/ppy/osu/issues/2852)
                Library.Load("libbass.so", Library.LoadFlags.RTLD_LAZY | Library.LoadFlags.RTLD_GLOBAL);
            }
        }
    }
}
