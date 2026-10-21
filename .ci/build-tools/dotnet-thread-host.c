#define _GNU_SOURCE
#include <dlfcn.h>
#include <limits.h>
#include <pthread.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>
#include "hostfxr.h"

struct invocation { int argc; char **argv; char root[PATH_MAX]; int result; };
static void *run(void *state) {
    struct invocation *call = state;
    dlopen("libm.so.6", RTLD_NOW | RTLD_GLOBAL);
    char library[PATH_MAX + 64];
    snprintf(library, sizeof(library), "%s/host/fxr/9.0.18/libhostfxr.so", call->root);
    void *handle = dlopen(library, RTLD_NOW | RTLD_LOCAL);
    if (!handle) { fprintf(stderr, "%s\n", dlerror()); call->result = 1; return NULL; }
    hostfxr_initialize_for_dotnet_command_line_fn initialize = dlsym(handle, "hostfxr_initialize_for_dotnet_command_line");
    hostfxr_run_app_fn execute = dlsym(handle, "hostfxr_run_app");
    hostfxr_close_fn close_host = dlsym(handle, "hostfxr_close");
    if (!initialize || !execute || !close_host) { call->result = 1; return NULL; }
    struct hostfxr_initialize_parameters parameters = { sizeof(parameters), call->argv[0], call->root };
    hostfxr_handle context = NULL;
    int rc = initialize(call->argc - 1, (const char **)call->argv + 1, &parameters, &context);
    if (rc < 0 || !context) { fprintf(stderr, "hostfxr initialization: 0x%x\n", rc); call->result = 1; return NULL; }
    call->result = execute(context);
    close_host(context);
    return NULL;
}
int main(int argc, char **argv) {
    if (argc < 2) { fprintf(stderr, "Usage: dotnet-thread-host managed.dll [args]\n"); return 1; }
    struct invocation call = { .argc = argc, .argv = argv, .result = 1 };
    const char *root = getenv("DOTNET_ROOT");
    if (root) snprintf(call.root, sizeof(call.root), "%s", root);
    else {
        char executable[PATH_MAX];
        if (!realpath(argv[0], executable)) { perror("realpath"); return 1; }
        char *slash = strrchr(executable, '/'); *slash = '\0';
        snprintf(call.root, sizeof(call.root), "%s/dotnet", executable);
    }
    pthread_attr_t attributes;
    pthread_attr_init(&attributes);
    pthread_attr_setstacksize(&attributes, 32 * 1024 * 1024);
    pthread_t thread;
    int rc = pthread_create(&thread, &attributes, run, &call);
    pthread_attr_destroy(&attributes);
    if (rc) { fprintf(stderr, "pthread_create: %s\n", strerror(rc)); return 1; }
    pthread_join(thread, NULL);
    return call.result;
}
