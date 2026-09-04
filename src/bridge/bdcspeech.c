/*
 * bdcspeech - a tiny GameMaker-callable bridge to Prism.
 *
 * GameMaker Studio 1.4 can only call external functions whose arguments and return
 * values are doubles or C strings (ty_real / ty_string). Prism's real API traffics in
 * structs and opaque pointers, which GML cannot express, so everything is hidden behind
 * this shim and the game only ever sees numbers and strings.
 *
 * Every entry point is __cdecl and returns a double, matching dll_cdecl / ty_real on the
 * GML side. Nothing here can throw, and every function is safe to call before init or
 * after shutdown - a mod that cannot speak must never be able to crash the game.
 */

#include <windows.h>
#include <string.h>
#include <prism.h>

#define BDC_EXPORT __declspec(dllexport)

static PrismContext *g_ctx;
static PrismBackend *g_backend;
static int g_use_output;
static int g_can_stop;
static char g_backend_name[128];

/* Prism reports "already initialized" as a non-fatal status; treat it as success. */
#ifndef PRISM_ALREADY_INITIALIZED
#define PRISM_ALREADY_INITIALIZED 15
#endif

/*
 * Brings up Prism and acquires the best available screen reader.
 * Returns 1 on success, 0 if no backend is usable (no screen reader running).
 * Safe to call repeatedly; later calls are no-ops once initialised.
 */
BDC_EXPORT double __cdecl bdc_init(void)
{
    PrismConfig cfg;
    uint64_t features;
    const char *name;

    if (g_backend != NULL)
        return 1.0;

    cfg = prism_config_init();
    g_ctx = prism_init(&cfg);
    if (g_ctx == NULL)
        return 0.0;

    /* Highest-priority backend that is actually present: NVDA, JAWS, SAPI, ... */
    g_backend = prism_registry_acquire_best(g_ctx);
    if (g_backend == NULL)
        return 0.0;

    {
        int rc = prism_backend_initialize(g_backend);
        if (rc != PRISM_OK && rc != PRISM_ALREADY_INITIALIZED) {
            /* Keep going: some backends report odd statuses yet still speak. */
        }
    }

    features = prism_backend_get_features(g_backend);
    g_use_output = (features & PRISM_BACKEND_SUPPORTS_OUTPUT) != 0;
    g_can_stop = (features & PRISM_BACKEND_SUPPORTS_STOP) != 0;

    name = prism_backend_name(g_backend);
    if (name != NULL) {
        strncpy(g_backend_name, name, sizeof(g_backend_name) - 1);
        g_backend_name[sizeof(g_backend_name) - 1] = '\0';
    }

    return 1.0;
}

/*
 * Speaks text. interrupt != 0 cuts off whatever is currently being said.
 * Returns 1 if the text was handed to a backend, 0 otherwise.
 */
BDC_EXPORT double __cdecl bdc_speak(const char *text, double interrupt)
{
    int rc;
    bool cut;

    if (g_backend == NULL || text == NULL || text[0] == '\0')
        return 0.0;

    cut = (interrupt != 0.0);

    /*
     * "output" routes through the screen reader's own speech+braille pipeline, which is
     * what a screen reader user expects; "speak" is speech only. Prefer output where the
     * backend offers it.
     */
    rc = g_use_output ? prism_backend_output(g_backend, text, cut)
                      : prism_backend_speak(g_backend, text, cut);

    return rc == PRISM_OK ? 1.0 : 0.0;
}

/* Stops speech immediately. Returns 1 if the backend could do it. */
BDC_EXPORT double __cdecl bdc_stop(void)
{
    if (g_backend == NULL || !g_can_stop)
        return 0.0;
    return prism_backend_stop(g_backend) == PRISM_OK ? 1.0 : 0.0;
}

/* Name of the backend in use, for the mod's own diagnostics. Never NULL. */
BDC_EXPORT const char *__cdecl bdc_backend_name(void)
{
    return g_backend_name;
}

/* Returns 1 when a backend is live and speech will actually be heard. */
BDC_EXPORT double __cdecl bdc_ready(void)
{
    return g_backend != NULL ? 1.0 : 0.0;
}

/*
 * Releases Prism. The game calls this on room end / game end.
 * Backends handed out by the registry are owned by it, so only the context is freed.
 */
BDC_EXPORT double __cdecl bdc_shutdown(void)
{
    if (g_backend != NULL && g_can_stop)
        prism_backend_stop(g_backend);

    if (g_ctx != NULL) {
        prism_shutdown(g_ctx);
        g_ctx = NULL;
    }

    g_backend = NULL;
    g_use_output = 0;
    g_can_stop = 0;
    g_backend_name[0] = '\0';
    return 1.0;
}

BOOL WINAPI DllMain(HINSTANCE inst, DWORD reason, LPVOID reserved)
{
    (void)inst;
    (void)reserved;
    /*
     * Deliberately no Prism work here. DllMain runs under the loader lock, and Prism
     * starts threads and COM - doing that from DllMain is a classic way to deadlock a
     * process at startup. GML calls bdc_init() when it is ready instead.
     */
    if (reason == DLL_PROCESS_DETACH)
        return TRUE;
    return TRUE;
}
