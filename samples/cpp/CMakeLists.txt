cmake_minimum_required(VERSION 3.20)
project(zui_sample_cpp LANGUAGES CXX)
set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
add_subdirectory(${CMAKE_CURRENT_SOURCE_DIR}/../../bindings/cpp zui_core)
add_executable(zui_sample WIN32
    main.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/../../examples/generated/showcase.g.cpp)
target_link_libraries(zui_sample PRIVATE zui)
