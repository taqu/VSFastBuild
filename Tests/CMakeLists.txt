cmake_minimum_required(VERSION 3.50)

set(CMAKE_CONFIGURATION_TYPES "Debug" "Release")

set(PROJECT_NAME tests)
project(${PROJECT_NAME})

set(HEADER_ROOT "${CMAKE_CURRENT_SOURCE_DIR}/src")
set(SOURCE_ROOT "${CMAKE_CURRENT_SOURCE_DIR}/src")
set(THIRDPARTY_ROOT "${CMAKE_CURRENT_SOURCE_DIR}/thirdparty")

set(PROJECT_ROOT "${CMAKE_CURRENT_SOURCE_DIR}")

add_subdirectory(test0)
add_subdirectory(test1)
add_subdirectory(test2)
