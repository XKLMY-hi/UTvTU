/// vst_bridge.cpp — VST3 host bridge for UTvTU
///
/// Architecture:
///   C# P/Invoke → flat C API → VST3::Hosting::Module + PlugProvider
///
/// Key design decisions:
///   • Plugin instance = VstBridgeInstance (lives in C# VstEffect)
///   • GUI param changes → atomic ring → vst_process consumes → inputParameterChanges
///   • State persistence: component->getState/setState via MemoryStream

#include "vst_bridge.h"

// VST3 SDK
#include "public.sdk/source/vst/hosting/module.h"
#include "public.sdk/source/vst/hosting/plugprovider.h"
#include "public.sdk/source/vst/hosting/hostclasses.h"
#include "public.sdk/source/vst/hosting/parameterchanges.h"
#include "public.sdk/source/common/memorystream.h"
#include "pluginterfaces/vst/ivstaudioprocessor.h"
#include "pluginterfaces/vst/ivstcomponent.h"
#include "pluginterfaces/vst/ivsteditcontroller.h"
#include "pluginterfaces/vst/vsttypes.h"
#include "pluginterfaces/gui/iplugview.h"
#include "base/source/fobject.h"
#include "base/source/fstring.h"

#include <atomic>
#include <cstdarg>
#include <cstdio>
#include <cstring>
#include <memory>
#include <string>
#include <vector>
#include <thread>

#ifdef _WIN32
#  include <windows.h>
#endif

using namespace Steinberg;

// ═════════════════════════════════════════════════════════════════════
//  Error
// ═════════════════════════════════════════════════════════════════════

static thread_local char g_lastError[512];

static void setError(const char* fmt, ...) {
    va_list args; va_start(args, fmt);
    std::vsnprintf(g_lastError, sizeof(g_lastError), fmt, args);
    va_end(args);
}
extern "C" const char* vst_last_error(void) {
    return g_lastError[0] ? g_lastError : nullptr;
}

// ═════════════════════════════════════════════════════════════════════
//  Lock-free parameter queue  (GUI thread → audio thread)
// ═════════════════════════════════════════════════════════════════════

static constexpr int kMaxParams = 4096;

/// Lock-free SPSC ring buffer for GUI→audio parameter updates.
/// Single producer (GUI thread, BridgeCompHandler::performEdit),
/// single consumer (audio thread, vst_process drain).
struct ParamQueue {
    struct Entry { int id; float value; };
    static constexpr size_t kRingSize = 2048;
    Entry ring[kRingSize] = {};
    std::atomic<size_t> writeIdx{0};  // producer cursor
    std::atomic<size_t> readIdx{0};   // consumer cursor

    void push(int id, float v) {
        size_t w = writeIdx.load(std::memory_order_relaxed);
        size_t r = readIdx.load(std::memory_order_acquire);
        if ((w - r) >= kRingSize) return; // full — drop silently
        ring[w % kRingSize] = {id, v};
        writeIdx.store(w + 1, std::memory_order_release);
    }
    // Drain all pending entries. O(pending), not O(kMaxParams).
    int drain(std::vector<std::pair<int,float>>& out) {
        out.clear();
        size_t r = readIdx.load(std::memory_order_relaxed);
        size_t w = writeIdx.load(std::memory_order_acquire);
        while (r != w) {
            auto& e = ring[r % kRingSize];
            out.emplace_back(e.id, e.value);
            r++;
        }
        readIdx.store(r, std::memory_order_release);
        return (int)out.size();
    }
};

// ═════════════════════════════════════════════════════════════════════


// BridgeCompHandler — routes GUI param changes into the per-instance ParamQueue.
// This is the CRITICAL path for "GUI knob → audio changes".
struct VstBridgeInstance;

class BridgeCompHandler : public Vst::IComponentHandler,
                           public Vst::IComponentHandler2 {
public:
    VstBridgeInstance* inst = nullptr;

    // IComponentHandler
    tresult PLUGIN_API beginEdit(Vst::ParamID) override { return kResultOk; }
    tresult PLUGIN_API performEdit(Vst::ParamID id, Vst::ParamValue v) override;
    tresult PLUGIN_API endEdit(Vst::ParamID) override { return kResultOk; }
    tresult PLUGIN_API restartComponent(int32) override { return kResultOk; }
    // IComponentHandler2
    tresult PLUGIN_API setDirty(TBool) override { return kResultOk; }
    tresult PLUGIN_API requestOpenEditor(FIDString) override { return kResultOk; }
    tresult PLUGIN_API startGroupEdit() override { return kResultOk; }
    tresult PLUGIN_API finishGroupEdit() override { return kResultOk; }

    tresult PLUGIN_API queryInterface(const TUID iid, void** obj) override {
        if (FUnknownPrivate::iidEqual(iid, Vst::IComponentHandler::iid) ||
            FUnknownPrivate::iidEqual(iid, Vst::IComponentHandler2::iid) ||
            FUnknownPrivate::iidEqual(iid, FUnknown::iid))
            { *obj = this; addRef(); return kResultTrue; }
        return kNoInterface;
    }
    uint32 PLUGIN_API addRef() override { return 1000; }
    uint32 PLUGIN_API release() override { return 1000; }
};


//  Instance
// ═════════════════════════════════════════════════════════════════════

struct VstBridgeInstance {
    VST3::Hosting::Module::Ptr  module;
    IPtr<Vst::IComponent>       component;
    IPtr<Vst::IAudioProcessor>  processor;
    IPtr<Vst::IEditController>  controller;
    Vst::PlugProvider*          plugProvider = nullptr;

    bool        isActivated  = false;
    int         numInputs    = 2;
    int         numOutputs   = 2;
    int         maxBlockSize = 4096;
    double      sampleRate   = 44100.0;
    int         paramCount   = 0;

    IPtr<IPlugView> editorView;
    bool            stateSynced = false;
    bool            editorWindowOpen = false;
    HWND            editorWindowHwnd = nullptr;   // vst_open_editor_window 的弹出窗口句柄

    // Per-instance param channel: GUI writes, audio thread reads
    ParamQueue   paramQueue;

    // Per-instance component handler (fixes global g_handler routing bug)
    BridgeCompHandler handler;

    // Temp buffers for deinterleaving
    std::vector<float> tmpCh0, tmpCh1;

    // Drain scratch (reused to avoid per-frame allocation)
    std::vector<std::pair<int,float>> drainedParams;
};

tresult PLUGIN_API BridgeCompHandler::performEdit(Vst::ParamID id, Vst::ParamValue v) {
    if (inst) inst->paramQueue.push((int)id, (float)v);
    return kResultOk;
}

// ═════════════════════════════════════════════════════════════════════
//  Host context
// ═════════════════════════════════════════════════════════════════════

static Vst::HostApplication g_hostApp;

class BridgePlugFrame : public IPlugFrame {
public:
    tresult PLUGIN_API resizeView(IPlugView* v, ViewRect* r) override {
        if (v && r) { v->onSize(r); return kResultOk; }
        return kInvalidArgument;
    }
    tresult PLUGIN_API queryInterface(const TUID iid, void** obj) override {
        if (FUnknownPrivate::iidEqual(iid, IPlugFrame::iid) ||
            FUnknownPrivate::iidEqual(iid, FUnknown::iid))
            { *obj = this; addRef(); return kResultTrue; }
        return kNoInterface;
    }
    uint32 PLUGIN_API addRef() override { return 1000; }
    uint32 PLUGIN_API release() override { return 1000; }
};
// Fixes: global g_handler caused param changes from plugin A to be routed to
// plugin B's queue when multiple editors were open simultaneously.

// ═════════════════════════════════════════════════════════════════════
//  State helpers
// ═════════════════════════════════════════════════════════════════════

static void syncComponentToController(VstBridgeInstance* inst) {
    if (!inst || !inst->component || !inst->controller || inst->stateSynced) return;
    MemoryStream ms;
    if (inst->component->getState(&ms) == kResultOk) {
        ms.seek(0, IBStream::kIBSeekSet, nullptr);
        inst->controller->setComponentState(&ms);
        inst->stateSynced = true;
    }
}

// ═════════════════════════════════════════════════════════════════════
//  Load / Unload
// ═════════════════════════════════════════════════════════════════════

extern "C" VstBridgeInstance* vst_load(const wchar_t* bundlePath) {
    g_lastError[0] = '\0';
    if (!bundlePath || !bundlePath[0]) { setError("null path"); return nullptr; }

    // B5: convert wide path (C# LPWStr) to UTF-8 for VST3 SDK
    std::wstring wpath(bundlePath);
    std::string path;
    {   int len = WideCharToMultiByte(CP_UTF8, 0, wpath.c_str(), -1, nullptr, 0, nullptr, nullptr);
        path.resize(len - 1);
        WideCharToMultiByte(CP_UTF8, 0, wpath.c_str(), -1, &path[0], len, nullptr, nullptr);
    }

    auto* inst = new (std::nothrow) VstBridgeInstance();
    if (!inst) { setError("oom"); return nullptr; }

    // 1. Module
    std::string err;
    inst->module = VST3::Hosting::Module::create(path, err);
    if (!inst->module) { setError("module: %s", err.c_str()); delete inst; return nullptr; }

    // 2. Context
    Vst::PluginContextFactory::instance().setPluginContext(&g_hostApp);
    auto& factory = inst->module->getFactory();
    factory.setHostContext(&g_hostApp);

    // 3. Find Audio Module Class
    VST3::Hosting::ClassInfo effCls;
    bool found = false;
    for (const auto& ci : factory.classInfos()) {
        if (ci.category() == "Audio Module Class") { effCls = ci; found = true; break; }
    }
    if (!found) { setError("no Audio Module Class"); delete inst; return nullptr; }

    // 4. PlugProvider (heap: lives until vst_unload)
    auto* provider = new Vst::PlugProvider(factory, effCls, true);
    if (!provider->initialize()) {
        setError("PlugProvider::initialize failed");
        delete provider; delete inst; return nullptr;
    }
    inst->plugProvider = provider;
    inst->component  = provider->getComponentPtr();
    inst->controller = provider->getControllerPtr();
    if (!inst->component || !inst->controller) {
        setError("null component/controller"); delete inst; return nullptr;
    }

    // 5. Processor
    inst->processor = FUnknownPtr<Vst::IAudioProcessor>(inst->component.get());
    if (!inst->processor) { setError("no IAudioProcessor"); delete inst; return nullptr; }

    // 6. Wire up per-instance handler (each instance has its own queue)
    inst->handler.inst = inst;
    inst->controller->setComponentHandler(&inst->handler);

    // 7. Params
    inst->paramCount = inst->controller->getParameterCount();
    if (inst->paramCount < 0) inst->paramCount = 0;

    return inst;
}

extern "C" void vst_unload(VstBridgeInstance* inst) {
    if (!inst) return;
    if (inst->isActivated && inst->component)
        inst->component->setActive(false);
    vst_close_editor(inst);
    inst->controller = nullptr;
    inst->processor  = nullptr;
    inst->component  = nullptr;
    inst->editorView = nullptr;
    if (inst->plugProvider) { delete inst->plugProvider; inst->plugProvider = nullptr; }
    inst->module.reset();
    delete inst;
}

// ═════════════════════════════════════════════════════════════════════
//  Setup / Activate
// ═════════════════════════════════════════════════════════════════════

extern "C" int vst_setup(VstBridgeInstance* inst, double sr, int maxBlock) {
    if (!inst || !inst->processor || !inst->component) return -1;
    g_lastError[0] = '\0';
    inst->sampleRate = sr; inst->maxBlockSize = maxBlock;

    Vst::ProcessSetup ps {};
    ps.processMode = Vst::kRealtime; ps.symbolicSampleSize = Vst::kSample32;
    ps.maxSamplesPerBlock = maxBlock; ps.sampleRate = sr;
    tresult r = inst->processor->setupProcessing(ps);
    if (r != kResultOk && r != kResultTrue) {
        setError("setupProcessing 0x%08X", r); return -1;
    }

    inst->numInputs = inst->numOutputs = 0;
    for (int dir : {Vst::kInput, Vst::kOutput}) {
        int n = inst->component->getBusCount(Vst::kAudio, dir);
        for (int i = 0; i < n; ++i) {
            Vst::BusInfo bi {};
            if (inst->component->getBusInfo(Vst::kAudio, dir, i, bi) == kResultOk
                && bi.busType == Vst::kMain) {
                inst->component->activateBus(Vst::kAudio, dir, i, true);
                (dir == Vst::kInput ? inst->numInputs : inst->numOutputs) += bi.channelCount;
            }
        }
    }
    if (inst->numInputs == 0) inst->numInputs = 2;
    if (inst->numOutputs == 0) inst->numOutputs = 2;
    inst->tmpCh0.resize(maxBlock); inst->tmpCh1.resize(maxBlock);
    return 0;
}

extern "C" int vst_activate(VstBridgeInstance* inst, int enable) {
    if (!inst || !inst->component) return -1;
    g_lastError[0] = '\0';
    tresult r = inst->component->setActive(enable != 0);
    if (r != kResultOk && r != kResultTrue) {
        setError("setActive(%d) 0x%08X", enable, r); return -1;
    }
    inst->isActivated = (enable != 0);
    return 0;
}


// B6: SEH-safe wrapper — must be in its own function (no local objects with dtors)
// so the compiler allows __try/__except.
static void call_process_safe(VstBridgeInstance* inst, Vst::ProcessData& pd) {
#ifdef _WIN32
    __try {
        inst->processor->process(pd);
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        setError("vst_process: plugin crashed (SEH)");
        inst->isActivated = false;
    }
#else
    inst->processor->process(pd);
#endif
}
// ═════════════════════════════════════════════════════════════════════
//  Audio Processing  (with param queue consumption)
// ═════════════════════════════════════════════════════════════════════

extern "C" void vst_process(VstBridgeInstance* inst, float* buffer, int frames) {
    if (!inst || !inst->processor || !inst->isActivated || frames <= 0) return;
    int nf = frames > inst->maxBlockSize ? inst->maxBlockSize : frames;

    // Deinterleave
    float* ch0 = inst->tmpCh0.data();
    float* ch1 = inst->tmpCh1.data();
    for (int i = 0; i < nf; ++i) { ch0[i] = buffer[i*2]; ch1[i] = buffer[i*2+1]; }

    float* inCh[2]  = { ch0, ch1 };
    float* outCh[2] = { ch0, ch1 };

    Vst::AudioBusBuffers ib {}, ob {};
    ib.numChannels = 2; ib.channelBuffers32 = inCh;
    ob.numChannels = 2; ob.channelBuffers32 = outCh;

    // ── Drain param queue → inputParameterChanges ──────────
    Vst::ParameterChanges inputChanges;
    inst->drainedParams.clear();
    inst->paramQueue.drain(inst->drainedParams);

    for (auto& [pid, value] : inst->drainedParams) {
        int32 idx = 0;
        Vst::IParamValueQueue* q = inputChanges.addParameterData(
            (Vst::ParamID)pid, idx);
        if (q) {
            int32 ptIdx = 0;
            q->addPoint(0, (Vst::ParamValue)value, ptIdx);
        }
    }

    Vst::ProcessData pd {};
    pd.processMode = Vst::kRealtime; pd.symbolicSampleSize = Vst::kSample32;
    pd.numSamples = nf; pd.numInputs = 1; pd.numOutputs = 1;
    pd.inputs = &ib; pd.outputs = &ob;
    pd.inputParameterChanges = &inputChanges;

    call_process_safe(inst, pd);
    if (!inst->isActivated) return;  // plugin crashed in SEH

    // Re-interleave
    for (int i = 0; i < nf; ++i) { buffer[i*2] = ch0[i]; buffer[i*2+1] = ch1[i]; }
}

extern "C" void vst_reset(VstBridgeInstance* inst) {
    if (!inst || !inst->component) return;
    bool was = inst->isActivated;
    if (was) inst->component->setActive(false);
    inst->processor->setupProcessing(Vst::ProcessSetup{});
    Vst::ProcessSetup ps {};
    ps.processMode = Vst::kRealtime; ps.symbolicSampleSize = Vst::kSample32;
    ps.maxSamplesPerBlock = inst->maxBlockSize; ps.sampleRate = inst->sampleRate;
    inst->processor->setupProcessing(ps);
    if (was) inst->component->setActive(true);
    inst->isActivated = was;
}

// ═════════════════════════════════════════════════════════════════════
//  Parameters
// ═════════════════════════════════════════════════════════════════════

extern "C" int vst_get_num_params(VstBridgeInstance* inst) {
    return inst ? inst->paramCount : 0;
}
extern "C" float vst_get_param(VstBridgeInstance* inst, int pid) {
    if (!inst || !inst->controller || pid < 0 || pid >= inst->paramCount) return 0.f;
    return inst->controller->getParamNormalized((Vst::ParamID)pid);
}
extern "C" void vst_set_param(VstBridgeInstance* inst, int pid, float v) {
    if (!inst || !inst->controller || pid < 0 || pid >= inst->paramCount) return;
    inst->controller->setParamNormalized((Vst::ParamID)pid, v);
}
extern "C" const char* vst_get_param_name(VstBridgeInstance* inst, int pid) {
    static thread_local char nb[128]; nb[0] = '\0';
    if (!inst || !inst->controller || pid < 0 || pid >= inst->paramCount) return nb;
    Vst::ParameterInfo pi {};
    if (inst->controller->getParameterInfo((Vst::ParamID)pid, pi) == kResultOk)
        String(pi.title).copyTo8(nb, 0, 127);
    return nb;
}
extern "C" int vst_get_num_inputs(VstBridgeInstance* inst) {
    return inst ? inst->numInputs : 0;
}
extern "C" int vst_get_num_outputs(VstBridgeInstance* inst) {
    return inst ? inst->numOutputs : 0;
}

// ═════════════════════════════════════════════════════════════════════
//  State persistence  (getState / setState)
// ═════════════════════════════════════════════════════════════════════

extern "C" int vst_save_state(VstBridgeInstance* inst, char* outBuf, int* ioSize) {
    if (!inst || !inst->component || !outBuf || !ioSize) return -1;
    g_lastError[0] = '\0';

    MemoryStream ms;
    if (inst->component->getState(&ms) != kResultOk) {
        setError("getState failed"); return -1;
    }
    TSize sz = ms.getSize();
    if (*ioSize < (int)sz) {
        setError("buffer too small: need %d, have %d", (int)sz, *ioSize);
        *ioSize = (int)sz;
        return -2; // caller should resize and retry
    }
    ms.seek(0, IBStream::kIBSeekSet, nullptr);
    int32 read = 0;
    ms.read(outBuf, (int32)sz, &read);
    *ioSize = read;
    return 0;
}

extern "C" int vst_restore_state(VstBridgeInstance* inst, const char* data, int size) {
    if (!inst || !inst->component || !data || size <= 0) return -1;
    g_lastError[0] = '\0';

    // MemoryStream with data constructor takes non-const void*
    // but setState only reads, so cast is safe.
    MemoryStream ms((void*)data, (TSize)size);
    if (inst->component->setState(&ms) != kResultOk) {
        setError("setState failed"); return -1;
    }
    // Re-sync to controller after restoring processor state
    inst->stateSynced = false;
    syncComponentToController(inst);
    return 0;
}

// ═════════════════════════════════════════════════════════════════════
//  Editor (low-level)
// ═════════════════════════════════════════════════════════════════════

extern "C" void* vst_open_editor(VstBridgeInstance* inst, void* parentHwnd) {
    if (!inst || !inst->controller) return nullptr;
    g_lastError[0] = '\0';
    syncComponentToController(inst);
    auto* v = inst->controller->createView(Vst::ViewType::kEditor);
    if (!v) { setError("createView null"); return nullptr; }
    inst->editorView = owned(v);
#ifdef _WIN32
    if (inst->editorView->isPlatformTypeSupported(kPlatformTypeHWND) != kResultTrue) {
        setError("no HWND"); inst->editorView = nullptr; return nullptr;
    }
    static BridgePlugFrame s_frame;
    inst->editorView->setFrame(&s_frame);
    if (inst->editorView->attached(parentHwnd, kPlatformTypeHWND) != kResultTrue) {
        setError("attach fail"); inst->editorView = nullptr; return nullptr;
    }
    ViewRect vr {};
    if (inst->editorView->getSize(&vr) == kResultTrue) {
        if (vr.getWidth() == 0 || vr.getHeight() == 0) vr = ViewRect(0,0,400,300);
        inst->editorView->onSize(&vr);
    }
#endif
    return inst->editorView.get();
}

extern "C" void vst_close_editor(VstBridgeInstance* inst) {
    if (!inst) return;
    // 关闭自托管弹出窗口（vst_open_editor_window 创建，独立窗口线程）。
    // 此前从不关闭——vst_unload 释放 inst 后窗口线程访问悬空内存 → use-after-free 崩溃
    //（复现：打开 VST 原生 GUI 后再次打开项目）。SendMessageW 同步等待窗口线程
    // 处理完 WM_CLOSE（→DestroyWindow→PostQuitMessage→消息循环退出），
    // WM_NCDESTROY 在 inst 释放前把 editorWindowOpen 置 false。
    if (inst->editorWindowOpen) {
        // 窗口线程化后：初始化（attached 等）在窗口线程进行——此处等待 hwnd
        // 就绪（最长 5s），防"初始化中卸载"的 use-after-free 竞态
        for (int i = 0; i < 50 && inst->editorWindowHwnd == nullptr; i++) Sleep(100);
        if (inst->editorWindowHwnd) {
            HWND hwnd = inst->editorWindowHwnd;
            inst->editorWindowHwnd = nullptr;
            SendMessageW(hwnd, WM_CLOSE, 0, 0);
        }
    }
    if (inst->editorView) {
        inst->editorView->setFrame(nullptr);
        inst->editorView->removed();
        inst->editorView = nullptr;
    }
}

// ═════════════════════════════════════════════════════════════════════
//  Self-hosted Win32 popup editor
// ═════════════════════════════════════════════════════════════════════

#ifdef _WIN32

struct EditorWinState {
    VstBridgeInstance* inst;
    HWND hwnd;
    IPtr<IPlugView> plugView;
    BridgePlugFrame plugFrame;
};

static LRESULT CALLBACK EditorWndProc(HWND hwnd, UINT msg, WPARAM wp, LPARAM lp) {
    auto* s = reinterpret_cast<EditorWinState*>(GetWindowLongPtrW(hwnd, GWLP_USERDATA));
    switch (msg) {
    case WM_CREATE:
        SetWindowLongPtrW(hwnd, GWLP_USERDATA,
            (LONG_PTR)((CREATESTRUCTW*)lp)->lpCreateParams);
        return 0;
    case WM_ERASEBKGND: return TRUE;
    case WM_PAINT: { PAINTSTRUCT ps; BeginPaint(hwnd, &ps); EndPaint(hwnd, &ps); return 0; }
    case WM_CLOSE:
        if (s && s->plugView) { s->plugView->setFrame(nullptr); s->plugView->removed(); s->plugView = nullptr; }
        DestroyWindow(hwnd);
        return 0;
    case WM_DESTROY: return 0;  // 线程生命周期由宿主(VstThread)管理——窗口销毁不杀线程
    case WM_NCDESTROY:
        if (s && s->inst) s->inst->editorWindowOpen = false;
        delete s; return 0;
    }
    return DefWindowProcW(hwnd, msg, wp, lp);
}

extern "C" int vst_open_editor_window(VstBridgeInstance* inst) {
    g_lastError[0] = '\0';
    if (!inst || !inst->controller) { setError("no controller"); return 0; }
    if (inst->editorWindowOpen) { setError("window already open"); return 0; }

    // 窗口在调用线程（UI）创建：插件 controller 在 vst_load 的 UI 线程创建，
    // createView/attached 期望同线程——窗口线程化会让线程敏感插件
    //（Persistent Q 等）打不开 / TDR Nova 卡死。UI 线程创建兼容所有插件；
    // 重插件（EQ 类）attached 初始化时 UI 短暂卡是插件固有成本。
    // 窗口消息由 Avalonia 的 UI 消息泵驱动（detach 线程仅兜底，收不到
    // 跨线程窗口消息——窗口消息实际由创建线程的消息队列处理）。
    if (!inst->isActivated && vst_activate(inst, 1) != 0) { setError("activate fail"); return 0; }
    syncComponentToController(inst);

    auto* view = inst->controller->createView(Vst::ViewType::kEditor);
    if (!view) { setError("createView null"); return 0; }
    IPtr<IPlugView> pv = owned(view);

    if (pv->isPlatformTypeSupported(kPlatformTypeHWND) != kResultTrue) { setError("no HWND"); return 0; }

    ViewRect vr {};
    if (pv->getSize(&vr) != kResultTrue) { setError("getSize fail"); return 0; }
    int w = vr.getWidth() > 0 ? vr.getWidth() : 500;
    int h = vr.getHeight() > 0 ? vr.getHeight() : 400;

    static bool s_reg = false;
    if (!s_reg) {
        WNDCLASSEXW wc {}; wc.cbSize = sizeof(wc);
        wc.lpfnWndProc = EditorWndProc; wc.hInstance = GetModuleHandleW(nullptr);
        wc.hCursor = LoadCursorW(nullptr, IDC_ARROW);
        wc.lpszClassName = L"OUVstEditor"; RegisterClassExW(&wc); s_reg = true;
    }

    auto* st = new EditorWinState{inst, nullptr, pv, {}};
    DWORD style = WS_OVERLAPPEDWINDOW | WS_CLIPCHILDREN | WS_CLIPSIBLINGS;
    RECT rect = {0,0,w,h}; AdjustWindowRect(&rect, style, FALSE);
    HWND hwnd = CreateWindowExW(0, L"OUVstEditor", L"VST Editor", style,
        CW_USEDEFAULT, CW_USEDEFAULT, rect.right-rect.left, rect.bottom-rect.top,
        nullptr, nullptr, GetModuleHandleW(nullptr), st);
    if (!hwnd) { delete st; setError("CreateWindow %lu", GetLastError()); return 0; }
    st->hwnd = hwnd;
    inst->editorWindowHwnd = hwnd;   // vst_close_editor 用它关闭窗口

    pv->setFrame(&st->plugFrame);
    if (pv->attached((void*)hwnd, kPlatformTypeHWND) != kResultTrue) {
        setError("attach fail"); pv->setFrame(nullptr); DestroyWindow(hwnd);
        inst->editorWindowHwnd = nullptr; return 0;
    }

    ViewRect vr2 {};
    if (pv->getSize(&vr2) == kResultTrue && (vr2.getWidth() != w || vr2.getHeight() != h))
        pv->onSize(&vr2);
    inst->editorWindowOpen = true;
    ShowWindow(hwnd, SW_SHOW); UpdateWindow(hwnd);
    // 窗口消息由宿主的 VST 专用线程消息泵处理（本函数调用线程 = controller
    // 创建线程 = 消息泵线程：createView/attached 的线程敏感检查通过，
    // 插件 PostMessage 等待的消息由同一线程泵出，无死锁）。
    return 1;
}
#else
extern "C" int vst_open_editor_window(VstBridgeInstance*) { setError("non-Windows"); return 0; }
#endif

// ═════════════════════════════════════════════════════════════════════
//  Probe
// ═════════════════════════════════════════════════════════════════════

static void ja(std::string& j, const char* k, const std::string& v) {
    if (j.size()>1) j+=','; j+='"'; j+=k; j+="\":\""; for (char c:v){if(c=='"'||c=='\\')j+='\\'; j+=c;} j+='"';
}
static void jo(std::string& j, const char* k) { if(j.size()>1)j+=','; j+='"'; j+=k; j+="\":["; }
static void jc(std::string& j) { j+=']'; }

extern "C" int vst_probe(const wchar_t* path, char* jsonBuf, int jsonBufSize) {
    g_lastError[0] = '\0';
    if (!path || !jsonBuf || jsonBufSize <= 0) return -1;
    jsonBuf[0] = '\0';
    // B5: convert wide path to UTF-8
    std::wstring wpath(path);
    std::string u8path;
    {   int len = WideCharToMultiByte(CP_UTF8, 0, wpath.c_str(), -1, nullptr, 0, nullptr, nullptr);
        u8path.resize(len - 1);
        WideCharToMultiByte(CP_UTF8, 0, wpath.c_str(), -1, &u8path[0], len, nullptr, nullptr);
    }
    std::string err;
    auto mod = VST3::Hosting::Module::create(u8path, err);
    if (!mod) { setError("%s", err.c_str()); return -1; }
    auto& f = mod->getFactory();
    std::string j = "{"; ja(j, "path", u8path); ja(j, "name", mod->getName()); ja(j, "vendor", "");
    j += ",\"classCount\":"; int cc = 0;
    for (const auto& c : f.classInfos()) if (c.category() == "Audio Module Class") cc++;
    j += std::to_string(cc); jo(j, "classes"); int idx = 0;
    for (const auto& c : f.classInfos()) {
        if (c.category() != "Audio Module Class") continue;
        if (idx++) j+=','; std::string cj="{";
        ja(cj, "cid", c.ID().toString()); ja(cj, "name", c.name());
        ja(cj, "category", c.category()); ja(cj, "vendor", c.vendor());
        auto& s = c.subCategories(); jo(cj, "subs");
        for (size_t si = 0; si < s.size(); ++si) {
            if (si) cj+=','; cj+='"'; for (char ch:s[si]){if(ch=='"'||ch=='\\')cj+='\\'; cj+=ch;} cj+='"';
        }
        jc(cj); cj+='}'; j+=cj;
    }
    jc(j); j+='}';
    int len = (int)j.size(); if (len >= jsonBufSize) len = jsonBufSize-1;
    std::memcpy(jsonBuf, j.c_str(), len); jsonBuf[len] = '\0';
    return 0;
}

#ifdef _WIN32
BOOL WINAPI DllMain(HINSTANCE, DWORD r, LPVOID) { return TRUE; (void)r; }
#endif
