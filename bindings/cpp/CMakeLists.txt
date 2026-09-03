cmake_minimum_required(VERSION 3.20)
project(zui_cpp LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# Envelope helpers - zero external dependency, unit-testable on its own.
add_library(zui_util STATIC zui_envelope.cpp)
target_include_directories(zui_util PUBLIC ${CMAKE_CURRENT_SOURCE_DIR})

# Host orchestration. An executable using Host(void*) must also link a backend
# TU (zui_webview2.cpp on Windows) or pass its own WebViewBackend.
add_library(zui STATIC zui.cpp)
target_link_libraries(zui PUBLIC zui_util)

# Copy the shared core assets next to consumers of this target.
add_custom_command(TARGET zui POST_BUILD
    COMMAND ${CMAKE_COMMAND} -E copy_directory
        ${CMAKE_CURRENT_SOURCE_DIR}/../../core
        $<TARGET_FILE_DIR:zui>/zui
    COMMENT "zUI: staging core CSS/JS")

option(ZUI_BUILD_TESTS "Build zUI C++ tests" OFF)
if(ZUI_BUILD_TESTS)
    add_executable(zui_envelope_test tests/envelope_test.cpp)
    target_link_libraries(zui_envelope_test PRIVATE zui_util)   # no backend needed
    enable_testing()
    add_test(NAME envelope COMMAND zui_envelope_test)
endif()
