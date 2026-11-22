// MIMESIS VR native helper: on Unity's render thread acquires/waits swapchain images, copies Unity textures
// into them, releases them and calls xrEndFrame. The main thread never touches the D3D11 immediate context.
#define COBJMACROS
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdint.h>
#include <d3d11.h>

#define MVR_MAX_COPIES 12
#define MVR_MAX_IMAGES 4

typedef int32_t (__stdcall *PFN_Acquire)(uint64_t swapchain, const void* info, uint32_t* index);
typedef int32_t (__stdcall *PFN_Wait)(uint64_t swapchain, const void* info);
typedef int32_t (__stdcall *PFN_Release)(uint64_t swapchain, const void* info);
typedef int32_t (__stdcall *PFN_EndFrame)(uint64_t session, const void* frameEndInfo);
typedef int32_t (__stdcall *PFN_WaitFrame)(uint64_t session, const void* waitInfo, void* frameState);
typedef int32_t (__stdcall *PFN_BeginFrame)(uint64_t session, const void* beginInfo);

#pragma pack(push, 8)
typedef struct MvrCopy {
    uint64_t swapchain;
    void*    src;
    int32_t  imageCount;
    int32_t  pad;
    void*    images[MVR_MAX_IMAGES];
} MvrCopy; // 56 bytes

typedef struct MvrJob {
    MvrCopy  copies[MVR_MAX_COPIES]; // 448
    int32_t  copyCount;              // 448
    volatile int32_t stage;          // 452: progress marker for the watchdog
    uint64_t session;                // 456
    void*    frameEndInfo;           // 464
    int32_t  doEndFrame;             // 472
    volatile int32_t done;           // 476
    int32_t  lastResult;             // 480
    int32_t  lastSwapchainError;     // 484
    int32_t  doWaitBegin;            // 488: after xrEndFrame, xrWaitFrame + xrBeginFrame for the next frame
    int32_t  beginResult;            // 492
    void*    frameState;             // XrFrameState* filled by xrWaitFrame
    int32_t  usCopies;               // timings of the last callback, microseconds
    int32_t  usEndFrame;
    int32_t  usWaitFrame;
    int32_t  usBeginFrame;
} MvrJob;
#pragma pack(pop)

typedef struct { int32_t type; void* next; } XrInfoBase;
typedef struct { int32_t type; void* next; int64_t timeout; } XrWaitInfo;

static ID3D11DeviceContext* g_ctx = NULL;
static double g_qpcToUs = 0.0;
static int32_t UsSince(LARGE_INTEGER a)
{
    LARGE_INTEGER b; QueryPerformanceCounter(&b);
    if (g_qpcToUs == 0.0) { LARGE_INTEGER f; QueryPerformanceFrequency(&f); g_qpcToUs = 1e6 / (double)f.QuadPart; }
    return (int32_t)((b.QuadPart - a.QuadPart) * g_qpcToUs);
}

// ---- Native watchdog (not affected by Mono GC suspension) ----
typedef struct MvrShared {
    volatile int32_t mainStage;   // written by managed main thread
    volatile int32_t mainFrames;  // incremented by managed main thread per VR frame
    volatile int32_t renderFrames;// incremented by render-thread callback
    volatile int32_t armed;
} MvrShared;
static MvrShared g_shared = {0};
static volatile MvrJob* g_lastJob = NULL;
static HANDLE g_wdThread = NULL;
static wchar_t g_wdPath[1024];

static void wd_write(const char* line)
{
    HANDLE h = CreateFileW(g_wdPath, FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE, NULL, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
    if (h == INVALID_HANDLE_VALUE) return;
    DWORD w; WriteFile(h, line, (DWORD)lstrlenA(line), &w, NULL);
    CloseHandle(h);
}

static DWORD WINAPI wd_loop(LPVOID p)
{
    int32_t lastMain = -1, lastRender = -1; DWORD lastChange = GetTickCount(); int reported = 0;
    char buf[512];
    for (;;)
    {
        Sleep(500);
        if (!g_shared.armed) { lastChange = GetTickCount(); reported = 0; continue; }
        int32_t m = g_shared.mainFrames, r = g_shared.renderFrames;
        if (m != lastMain || r != lastRender)
        {
            if (reported) { SYSTEMTIME t; GetLocalTime(&t); wsprintfA(buf, "%02d:%02d:%02d native-wd: recovered (main %d, render %d)\r\n", t.wHour, t.wMinute, t.wSecond, m, r); wd_write(buf); }
            lastMain = m; lastRender = r; lastChange = GetTickCount(); reported = 0; continue;
        }
        DWORD stall = GetTickCount() - lastChange;
        if (stall > 2000 && !reported)
        {
            int32_t js = g_lastJob ? g_lastJob->stage : -1;
            SYSTEMTIME t; GetLocalTime(&t);
            wsprintfA(buf, "%02d:%02d:%02d native-wd: STALL %lu ms. main frames %d (main stage %d), render callbacks %d, render stage %d\r\n",
                      t.wHour, t.wMinute, t.wSecond, stall, m, g_shared.mainStage, r, js);
            wd_write(buf);
            reported = 1;
        }
    }
    return 0;
}

__declspec(dllexport) void* MVR_GetShared(void) { return (void*)&g_shared; }

__declspec(dllexport) int32_t MVR_StartWatchdog(const wchar_t* path)
{
    if (g_wdThread) return 1;
    lstrcpynW(g_wdPath, path, 1023);
    char hdr[] = "=== native watchdog started ===\r\n";
    wd_write(hdr);
    g_wdThread = CreateThread(NULL, 0, wd_loop, NULL, 0, NULL);
    return g_wdThread ? 1 : 0;
}
static PFN_Acquire g_acquire = NULL;
static PFN_Wait g_wait = NULL;
static PFN_Release g_release = NULL;
static PFN_EndFrame g_endFrame = NULL;
static PFN_WaitFrame g_waitFrame = NULL;
static PFN_BeginFrame g_beginFrame = NULL;

__declspec(dllexport) int32_t MVR_Init2(void* d3d11Device, void* pfnAcquire, void* pfnWait, void* pfnRelease, void* pfnEndFrame)
{
    g_acquire = (PFN_Acquire)pfnAcquire;
    g_wait = (PFN_Wait)pfnWait;
    g_release = (PFN_Release)pfnRelease;
    g_endFrame = (PFN_EndFrame)pfnEndFrame;
    if (g_ctx) { ID3D11DeviceContext_Release(g_ctx); g_ctx = NULL; }
    if (d3d11Device) ID3D11Device_GetImmediateContext((ID3D11Device*)d3d11Device, &g_ctx);
    return (g_ctx && g_acquire && g_wait && g_release && g_endFrame) ? 1 : 0;
}

__declspec(dllexport) int32_t MVR_JobSize2(void) { return (int32_t)sizeof(MvrJob); }

__declspec(dllexport) int32_t MVR_SetFrameFuncs(void* pfnWaitFrame, void* pfnBeginFrame)
{
    g_waitFrame = (PFN_WaitFrame)pfnWaitFrame;
    g_beginFrame = (PFN_BeginFrame)pfnBeginFrame;
    return (g_waitFrame && g_beginFrame) ? 1 : 0;
}

static void __stdcall MVR_OnRenderEvent(int eventId, void* data)
{
    MvrJob* j = (MvrJob*)data;
    if (!j) return;
    j->stage = 1; // callback entered
    g_lastJob = j;
    XrInfoBase acq = { 55, NULL };            // XR_TYPE_SWAPCHAIN_IMAGE_ACQUIRE_INFO
    XrWaitInfo wait = { 56, NULL, 0x7fffffffffffffffLL }; // XR_TYPE_SWAPCHAIN_IMAGE_WAIT_INFO, infinite
    XrInfoBase rel = { 57, NULL };            // XR_TYPE_SWAPCHAIN_IMAGE_RELEASE_INFO
    LARGE_INTEGER t0; QueryPerformanceCounter(&t0);
    for (int i = 0; i < j->copyCount && i < MVR_MAX_COPIES; i++)
    {
        MvrCopy* c = &j->copies[i];
        if (!c->swapchain || !c->src) continue;
        uint32_t idx = 0;
        j->stage = 10 + i * 10 + 1; // acquire
        int32_t r = g_acquire(c->swapchain, &acq, &idx);
        if (r < 0) { j->lastSwapchainError = r; continue; }
        j->stage = 10 + i * 10 + 2; // wait
        r = g_wait(c->swapchain, &wait);
        if (r < 0) { j->lastSwapchainError = r; g_release(c->swapchain, &rel); continue; }
        j->stage = 10 + i * 10 + 3; // copy
        if (g_ctx && idx < (uint32_t)c->imageCount && idx < MVR_MAX_IMAGES && c->images[idx])
            ID3D11DeviceContext_CopyResource(g_ctx, (ID3D11Resource*)c->images[idx], (ID3D11Resource*)c->src);
        j->stage = 10 + i * 10 + 4; // release
        g_release(c->swapchain, &rel);
    }
    j->usCopies = UsSince(t0);
    j->stage = 200; // xrEndFrame
    QueryPerformanceCounter(&t0);
    if (j->doEndFrame && g_endFrame && j->frameEndInfo)
        j->lastResult = g_endFrame(j->session, j->frameEndInfo);
    j->usEndFrame = UsSince(t0);
    if (j->doWaitBegin && g_waitFrame && g_beginFrame && j->frameState)
    {
        XrInfoBase wfi = { 33, NULL };   // XR_TYPE_FRAME_WAIT_INFO
        XrInfoBase bfi = { 46, NULL };   // XR_TYPE_FRAME_BEGIN_INFO
        *(int32_t*)j->frameState = 44;  // XR_TYPE_FRAME_STATE
        j->stage = 210; // xrWaitFrame
        QueryPerformanceCounter(&t0);
        int32_t r = g_waitFrame(j->session, &wfi, j->frameState);
        j->usWaitFrame = UsSince(t0);
        QueryPerformanceCounter(&t0);
        if (r >= 0) { j->stage = 220; r = g_beginFrame(j->session, &bfi); } // xrBeginFrame
        j->usBeginFrame = UsSince(t0);
        j->beginResult = r;
    }
    j->stage = 300; // done
    InterlockedIncrement((volatile LONG*)&g_shared.renderFrames);
    InterlockedExchange((volatile LONG*)&j->done, 1);
}

__declspec(dllexport) void* MVR_GetRenderEventFunc(void) { return (void*)MVR_OnRenderEvent; }

BOOL WINAPI DllMain(HINSTANCE h, DWORD r, LPVOID p) { return TRUE; }
