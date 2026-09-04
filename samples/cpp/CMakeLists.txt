cmake_minimum_required(VERSION 3.20)
project(zui_sample_cpp LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# The platform-independent zUI core.
add_subdirectory(${CMAKE_CURRENT_SOURCE_DIR}/../../bindings/cpp zui_core)

add_executable(zui_sample WIN32
    main.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/../../bindings/cpp/zui_webview2.cpp
)
target_link_libraries(zui_sample PRIVATE zui)

# --- WebView2 SDK + WIL -------------------------------------------------------
# Point these at your SDK install (NuGet packages extract to packages/…):
#   -DWEBVIEW2_DIR=C:/path/to/Microsoft.Web.WebView2
#   -DWIL_DIR=C:/path/to/Microsoft.Windows.ImplementationLibrary
if(DEFINED WEBVIEW2_DIR)
    target_include_directories(zui_sample PRIVATE ${WEBVIEW2_DIR}/build/native/include)
    target_link_libraries(zui_sample PRIVATE
        ${WEBVIEW2_DIR}/build/native/x64/WebView2LoaderStatic.lib)
endif()
if(DEFINED WIL_DIR)
    target_include_directories(zui_sample PRIVATE ${WIL_DIR}/include)
endif()

target_link_libraries(zui_sample PRIVATE version shlwapi)

# Stage the core assets + showcase next to the exe. core/ is staged twice:
# 'zui/' is the canonical virtual-root name; 'core/' keeps the bundled showcase's
# ../core/... links resolving (a host app writes its own doc against zui/).
add_custom_command(TARGET zui_sample POST_BUILD
    COMMAND ${CMAKE_COMMAND} -E copy_directory
        ${CMAKE_CURRENT_SOURCE_DIR}/../../core $<TARGET_FILE_DIR:zui_sample>/zui
    COMMAND ${CMAKE_COMMAND} -E copy_directory
        ${CMAKE_CURRENT_SOURCE_DIR}/../../core $<TARGET_FILE_DIR:zui_sample>/core
    COMMAND ${CMAKE_COMMAND} -E copy_directory
        ${CMAKE_CURRENT_SOURCE_DIR}/../../showcase $<TARGET_FILE_DIR:zui_sample>/showcase
    COMMENT "zUI sample: staging assets")

if(DEFINED WEBVIEW2_DIR)
    add_custom_command(TARGET zui_sample POST_BUILD
        COMMAND ${CMAKE_COMMAND} -E copy_if_different
            ${WEBVIEW2_DIR}/build/native/x64/WebView2Loader.dll $<TARGET_FILE_DIR:zui_sample>
        COMMENT "zUI sample: staging WebView2Loader.dll")
endif()
