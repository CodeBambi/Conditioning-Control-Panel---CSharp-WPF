using System;
using System.Collections.Generic;

namespace ConditioningControlPanel
{
    /// <summary>
    /// Why the offline speech model is or is not usable, mirrored into Core for the views that
    /// report it. The head's own <c>SpeechService.ModelStatus</c> maps onto this one for one:
    /// "there is no model" and "there IS a model and it refused to load" have to read differently,
    /// or users get told to install what they already installed.
    /// </summary>
    public enum CoreSpeechModelStatus
    {
        /// <summary>Not probed yet - nothing has asked for speech this run, or no head is attached.</summary>
        NotProbed,
        /// <summary>A model loaded; speech can run.</summary>
        Ok,
        /// <summary>Nothing under the model root looks like a speech model.</summary>
        NoModelFound,
        /// <summary>A model directory was found, but the engine refused to load it.</summary>
        LoadFailed,
    }

    /// <summary>A selectable microphone. Index -1 = the OS default capture device.</summary>
    public readonly record struct SpeechInputDevice(int Index, string Name);

    /// <summary>
    /// The speech-capability seam: the four things ported views ask before offering voice input.
    /// The service itself is Vosk over NAudio/WASAPI on Windows and stays in the head - it owns a
    /// capture device - so this carries capability only, never a listen call.
    ///
    /// <para>Unseeded means "no speech on this head", answered honestly rather than optimistically:
    /// no engine, no capture device, an empty device list, and
    /// <see cref="CoreSpeechModelStatus.NotProbed"/> - which is also what the Windows service
    /// reports once disposed. Never <c>Ok</c>, never null. A view that asks lands on its real
    /// "no microphone detected" branch instead of offering voice it cannot deliver.</para>
    /// </summary>
    public static class CoreSpeech
    {
        public static volatile Func<bool>? IsAvailableProvider;
        public static volatile Func<bool>? HasCaptureDeviceProvider;
        public static volatile Func<CoreSpeechModelStatus>? ModelStatusProvider;
        public static volatile Func<IReadOnlyList<SpeechInputDevice>>? EnumerateInputDevicesProvider;

        /// <summary>True when recognition can actually run: a model is loaded and a mic exists.
        /// Says nothing about consent - callers gate on that themselves.</summary>
        public static bool IsAvailable
        {
            get { try { return IsAvailableProvider?.Invoke() ?? false; } catch { return false; } }
        }

        /// <summary>Whether the OS reports at least one audio capture device.</summary>
        public static bool HasCaptureDevice
        {
            get { try { return HasCaptureDeviceProvider?.Invoke() ?? false; } catch { return false; } }
        }

        /// <summary>Why the model half of <see cref="IsAvailable"/> is where it is. Deliberately
        /// says nothing about the microphone - ask <see cref="HasCaptureDevice"/> and report a
        /// missing mic first, since it makes the model question moot.</summary>
        public static CoreSpeechModelStatus ModelStatus
        {
            get
            {
                try { return ModelStatusProvider?.Invoke() ?? CoreSpeechModelStatus.NotProbed; }
                catch { return CoreSpeechModelStatus.NotProbed; }
            }
        }

        /// <summary>The microphones a picker can offer, OS default first. Empty with no head,
        /// which is the truth: nothing here can open a device.</summary>
        public static IReadOnlyList<SpeechInputDevice> EnumerateInputDevices()
        {
            try { return EnumerateInputDevicesProvider?.Invoke() ?? Array.Empty<SpeechInputDevice>(); }
            catch { return Array.Empty<SpeechInputDevice>(); }
        }
    }
}
