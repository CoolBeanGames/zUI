cmake_minimum_required(VERSION 3.20)
project(zui_cpp LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# Envelope helpers - zero external dependency, unit-testable on its own.
add_library(zui_util STATIC zui_envelope.cpp)
target_include_directories(zui_util PUBLIC ${CMAKE_CURRENT_SOURCE_DIR})

# Native Win32 control runtime. No browser SDK or runtime dependency.
add_library(zui STATIC zui.cpp)
target_link_libraries(zui PUBLIC zui_util comctl32 user32 gdi32)

option(ZUI_BUILD_TESTS "Build zUI C++ tests" OFF)
if(ZUI_BUILD_TESTS)
    add_executable(zui_envelope_test tests/envelope_test.cpp)
    target_link_libraries(zui_envelope_test PRIVATE zui_util)   # no backend needed
    enable_testing()
    add_test(NAME envelope COMMAND zui_envelope_test)

    add_executable(zui_host_test tests/host_test.cpp)
    target_link_libraries(zui_host_test PRIVATE zui)
    add_test(NAME host COMMAND zui_host_test)

    add_executable(zui_mutation_test tests/mutation_test.cpp)
    target_link_libraries(zui_mutation_test PRIVATE zui)
    add_test(NAME mutation COMMAND zui_mutation_test)
endif()
