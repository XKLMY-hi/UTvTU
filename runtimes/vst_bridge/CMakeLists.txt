cmake_minimum_required(VERSION 3.20)

project(vst_bridge
    VERSION 1.0.0
    DESCRIPTION "VST3 host bridge for UTvTU"
    LANGUAGES CXX)

# ═══════════════════════════════════════════════════════════════
#  C++ standard
# ═══════════════════════════════════════════════════════════════
set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

# ═══════════════════════════════════════════════════════════════
#  VST3 SDK path
# ═══════════════════════════════════════════════════════════════
set(VST3SDK "${CMAKE_CURRENT_SOURCE_DIR}/../vst3sdk")
if(NOT EXISTS "${VST3SDK}/pluginterfaces/vst/ivstaudioprocessor.h")
    message(FATAL_ERROR
        "VST3 SDK not found at ${VST3SDK}.  Clone it with:\n"
        "  git clone --depth=1 --branch v3.8.0_build_66 \\\n"
        "    https://github.com/steinbergmedia/vst3sdk.git runtimes/vst3sdk\n"
        "  cd runtimes/vst3sdk && git submodule update --init --recursive --depth=1")
endif()
message(STATUS "VST3 SDK: ${VST3SDK}")

# ═══════════════════════════════════════════════════════════════
#  SDK include directories
# ═══════════════════════════════════════════════════════════════
set(VST3_INCLUDES
    "${VST3SDK}"
    "${VST3SDK}/base/source"
    "${VST3SDK}/base/thread/include"
    "${VST3SDK}/pluginterfaces"
    "${VST3SDK}/pluginterfaces/base"
    "${VST3SDK}/public.sdk"
    "${VST3SDK}/public.sdk/source"
    "${VST3SDK}/public.sdk/source/vst"
    "${VST3SDK}/public.sdk/source/vst/hosting"
)

# ═══════════════════════════════════════════════════════════════
#  SDK source files needed for VST3 hosting
# ═══════════════════════════════════════════════════════════════
set(VST3_HOSTING_SOURCES
    # Core hosting
    "${VST3SDK}/public.sdk/source/vst/hosting/module.cpp"
    "${VST3SDK}/public.sdk/source/vst/hosting/module_win32.cpp"
    "${VST3SDK}/public.sdk/source/vst/hosting/plugprovider.cpp"
    "${VST3SDK}/public.sdk/source/vst/hosting/eventlist.cpp"
    "${VST3SDK}/public.sdk/source/vst/hosting/parameterchanges.cpp"
    "${VST3SDK}/public.sdk/source/vst/hosting/processdata.cpp"
    "${VST3SDK}/public.sdk/source/vst/hosting/connectionproxy.cpp"
    "${VST3SDK}/public.sdk/source/vst/hosting/pluginterfacesupport.cpp"
    "${VST3SDK}/public.sdk/source/vst/hosting/hostclasses.cpp"

    # VST interface IIDs
    "${VST3SDK}/public.sdk/source/vst/vstinitiids.cpp"
    # VST utility
    "${VST3SDK}/public.sdk/source/vst/utility/stringconvert.cpp"

    # Common
    "${VST3SDK}/public.sdk/source/common/commoniids.cpp"
    "${VST3SDK}/public.sdk/source/common/commonstringconvert.cpp"
    "${VST3SDK}/public.sdk/source/common/memorystream.cpp"
    "${VST3SDK}/public.sdk/source/common/pluginview.cpp"
    "${VST3SDK}/public.sdk/source/common/readfile.cpp"
    "${VST3SDK}/public.sdk/source/common/systemclipboard_win32.cpp"
    "${VST3SDK}/public.sdk/source/common/threadchecker_win32.cpp"

    # Pluginterfaces base
    "${VST3SDK}/pluginterfaces/base/conststringtable.cpp"
    "${VST3SDK}/pluginterfaces/base/coreiids.cpp"
    "${VST3SDK}/pluginterfaces/base/funknown.cpp"
    "${VST3SDK}/pluginterfaces/base/ustring.cpp"

    # Base (foundation classes)
    "${VST3SDK}/base/source/baseiids.cpp"
    "${VST3SDK}/base/source/fbuffer.cpp"
    "${VST3SDK}/base/source/fdebug.cpp"
    "${VST3SDK}/base/source/fdynlib.cpp"
    "${VST3SDK}/base/source/fobject.cpp"
    "${VST3SDK}/base/source/fstreamer.cpp"
    "${VST3SDK}/base/source/fstring.cpp"
    "${VST3SDK}/base/source/timer.cpp"
    "${VST3SDK}/base/source/updatehandler.cpp"

    # Base threading
    "${VST3SDK}/base/thread/source/fcondition.cpp"
    "${VST3SDK}/base/thread/source/flock.cpp"
)

# ═══════════════════════════════════════════════════════════════
#  Bridge DLL target
# ═══════════════════════════════════════════════════════════════
add_library(vst_bridge SHARED
    src/vst_bridge.cpp
    ${VST3_HOSTING_SOURCES}
)

target_include_directories(vst_bridge PRIVATE
    ${CMAKE_CURRENT_SOURCE_DIR}/include
    ${VST3_INCLUDES}
)

target_compile_definitions(vst_bridge PRIVATE
    $<$<BOOL:${WIN32}>:WINDOWS>
    $<$<BOOL:${WIN32}>:_WIN32_WINNT=0x0A00>
    $<$<BOOL:${WIN32}>:NOMINMAX>
    $<$<BOOL:${WIN32}>:_CRT_SECURE_NO_WARNINGS>
    RELEASE=1
    SMTG_DONT_USE_DYLIB=1
)

# Windows: link against system libs needed for LoadLibrary etc.
if(WIN32)
    target_link_libraries(vst_bridge PRIVATE
        ole32
        user32
        shell32
        advapi32
    )
endif()

# Release optimisation
if(MSVC)
    target_compile_options(vst_bridge PRIVATE /O2 /GL)
    target_link_options(vst_bridge PRIVATE
        /OPT:REF /OPT:ICF /LTCG
    )
    set_property(TARGET vst_bridge PROPERTY
        MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
endif()

# ═══════════════════════════════════════════════════════════════
#  Install: copy built DLL to runtimes/win-x64/native/
# ═══════════════════════════════════════════════════════════════
install(TARGETS vst_bridge
    RUNTIME DESTINATION "${CMAKE_SOURCE_DIR}/../win-x64/native"
)

message(STATUS "vst_bridge configured successfully")
message(STATUS "  Build:   ${CMAKE_BINARY_DIR}")
message(STATUS "  Install: ${CMAKE_SOURCE_DIR}/../win-x64/native")
