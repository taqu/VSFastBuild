cmake_minimum_required(VERSION 3.50)

set(CMAKE_CONFIGURATION_TYPES "Debug" "Release")

set(PROJECT_NAME test0)
project(${PROJECT_NAME})

set(HEADER_ROOT "${CMAKE_CURRENT_SOURCE_DIR}")
set(SOURCE_ROOT "${CMAKE_CURRENT_SOURCE_DIR}")


# Sources
set(HEADERS
    "${HEADER_ROOT}/framework.h"
    "${HEADER_ROOT}/pch.h"
    "${HEADER_ROOT}/test0.h")
set(SOURCES
    "${SOURCE_ROOT}/test0.cpp")

source_group("include" FILES ${HEADERS})
source_group("src" FILES ${SOURCES})

set(FILES ${HEADERS} ${SOURCES})

set(OUTPUT_DIRECTORY "${CMAKE_CURRENT_SOURCE_DIR}/lib")
set(CMAKE_RUNTIME_OUTPUT_DIRECTORY_DEBUG "${OUTPUT_DIRECTORY}")
set(CMAKE_RUNTIME_OUTPUT_DIRECTORY_RELEASE "${OUTPUT_DIRECTORY}")

add_library(${PROJECT_NAME} STATIC ${FILES})

target_precompile_headers(${PROJECT_NAME} PUBLIC "${HEADER_ROOT}/pch.h")

if(MSVC)
    set(DEFAULT_CXX_FLAGS "/DWIN32 /D_WINDOWS /D_MSBC /W4 /WX- /nologo /fp:precise /arch:AVX2 /Zc:wchar_t /TP /Gd /utf-8")
    if("1800" VERSION_LESS MSVC_VERSION)
        set(DEFAULT_CXX_FLAGS "${DEFAULT_CXX_FLAGS} /EHsc")
    endif()

    set(CMAKE_CXX_FLAGS "${DEFAULT_CXX_FLAGS}")
    set(CMAKE_CXX_FLAGS_DEBUG "/D_DEBUG /MDd /Zi /Ob0 /Od /RTC1 /Gy /GR- /GS /Gm-")
    set(CMAKE_CXX_FLAGS_RELEASE "/MD /O2 /GL /GR- /DNDEBUG")
    #target_link_libraries(${PROJECT_NAME})

elseif(UNIX)
    set(DEFAULT_CXX_FLAGS "-Wall -O0 -g -std=c++20 -std=gnu++20 -march=native")
    set(CMAKE_CXX_FLAGS "${DEFAULT_CXX_FLAGS}")
    #target_link_libraries(${PROJECT_NAME})
elseif(APPLE)
endif()

set_property(DIRECTORY PROPERTY VS_STARTUP_PROJECT ${PROJECT_NAME})
set_property(TARGET ${PROJECT_NAME} PROPERTY CXX_STANDARD 23)
set_target_properties(${PROJECT_NAME}
    PROPERTIES
        OUTPUT_NAME_DEBUG "${PROJECT_NAME}" OUTPUT_NAME_RELEASE "${PROJECT_NAME}"
        VS_DEBUGGER_WORKING_DIRECTORY "${OUTPUT_DIRECTORY}")

