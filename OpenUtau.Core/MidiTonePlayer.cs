using System;
using System.Runtime.InteropServices;

namespace OpenUtau.Core {
    /// <summary>
    /// Windows 自带钢琴音源：系统 MIDI 合成器（Microsoft GS Wavetable Synth）
    /// 的 GM 钢琴音色（Program 0 = Acoustic Grand Piano），经 winmm MIDI 映射器播放。
    ///
    /// 打开系统默认 MIDI 设备（MIDI_MAPPER = -1）；无 MIDI 设备（精简系统等）时
    /// <see cref="IsAvailable"/> = false，调用方回退默认正弦波（ToneGenerator）。
    /// 支持多音同时发声（拖拽跨键时前后音重叠，真实钢琴感）。
    /// </summary>
    public sealed class MidiTonePlayer {
        const uint MIDI_MAPPER = 0xFFFFFFFF;      // -1：系统默认 MIDI 输出设备
        const byte GM_ACOUSTIC_GRAND_PIANO = 0;   // GM Program 0
        const byte Velocity = 100;                // 固定力度（中强）

        [DllImport("winmm.dll")]
        static extern uint midiOutOpen(out IntPtr handle, uint deviceId, IntPtr callback, IntPtr instance, uint flags);
        [DllImport("winmm.dll")]
        static extern uint midiOutClose(IntPtr handle);
        [DllImport("winmm.dll")]
        static extern uint midiOutShortMsg(IntPtr handle, uint msg);
        [DllImport("winmm.dll")]
        static extern uint midiOutReset(IntPtr handle);

        IntPtr _handle;
        public bool IsAvailable { get; private set; }

        public MidiTonePlayer() {
            if (!OperatingSystem.IsWindows()) return;
            // MMSYSERR_NODEV（无设备）等任何失败 → 不可用 → 回退正弦波
            uint err = midiOutOpen(out _handle, MIDI_MAPPER, IntPtr.Zero, IntPtr.Zero, 0);
            IsAvailable = err == 0;
            if (IsAvailable) {
                // 切到 GM 钢琴音色（channel 0, program 0）
                midiOutShortMsg(_handle, GM_ACOUSTIC_GRAND_PIANO | 0x00C0u);
            }
        }

        /// <summary>NoteOn（channel 0）。</summary>
        public void PlayTone(int note) {
            if (!IsAvailable) return;
            midiOutShortMsg(_handle, 0x0090u | ((uint)note << 8) | ((uint)Velocity << 16));
        }

        /// <summary>NoteOff（channel 0）。</summary>
        public void EndTone(int note) {
            if (!IsAvailable) return;
            midiOutShortMsg(_handle, 0x0080u | ((uint)note << 8));
        }

        /// <summary>全部 NoteOff（紧急停止所有发声）。</summary>
        public void EndAllTones() {
            if (!IsAvailable) return;
            midiOutReset(_handle);
        }
    }
}
