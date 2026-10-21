cmake_minimum_required(VERSION 3.20)
project(bakery_linux_native LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)

option(BAKERY_BUILD_GLUE "Build libbakery_glue.so" ON)
option(BAKERY_BUILD_FRENDER "Build libfrender.so" ON)
option(BAKERY_FRENDER_WINE "Build libfrender.so as the Wine-bridge proxy" ON)
option(BAKERY_BUILD_TOOLS "Build CLI tools" OFF)

get_filename_component(BAKERY_ROOT "${CMAKE_CURRENT_SOURCE_DIR}/.." ABSOLUTE)
set(BAKERY_LIB_DIR "${BAKERY_ROOT}/dist~/lib")
set(BAKERY_BIN_DIR "${BAKERY_ROOT}/dist~/bin")

if(BAKERY_BUILD_GLUE)
  find_library(LZ4_LIBRARY NAMES lz4 REQUIRED)
  add_library(bakery_glue SHARED "${BAKERY_ROOT}/src/glue/bakery_glue.cpp")
  target_include_directories(bakery_glue PUBLIC "${BAKERY_ROOT}/src/glue")
  target_link_libraries(bakery_glue PRIVATE pthread ${LZ4_LIBRARY})
  set_target_properties(bakery_glue PROPERTIES
    OUTPUT_NAME bakery_glue
    LIBRARY_OUTPUT_DIRECTORY "${BAKERY_LIB_DIR}")
endif()

if(BAKERY_BUILD_FRENDER)
  if(BAKERY_FRENDER_WINE)
    add_library(frender SHARED "${BAKERY_ROOT}/src/frender/frender_proxy.cpp")
    target_include_directories(frender PUBLIC "${BAKERY_ROOT}/src/frender" "${BAKERY_ROOT}/src/wine")
  else()
    add_library(frender SHARED "${BAKERY_ROOT}/src/frender/frender_stub.cpp")
    target_include_directories(frender PUBLIC "${BAKERY_ROOT}/src/frender")
  endif()
  target_link_libraries(frender PRIVATE pthread)
  set_target_properties(frender PROPERTIES
    OUTPUT_NAME frender
    LIBRARY_OUTPUT_DIRECTORY "${BAKERY_LIB_DIR}")
endif()

if(BAKERY_BUILD_TOOLS)
  add_executable(ftServer "${BAKERY_ROOT}/src/tools/ftServer.cpp")
  set_target_properties(ftServer PROPERTIES RUNTIME_OUTPUT_DIRECTORY "${BAKERY_BIN_DIR}")
endif()

if(BAKERY_BUILD_GLUE AND BAKERY_BUILD_FRENDER)
  add_custom_target(install-libs
    COMMAND bash "${CMAKE_CURRENT_SOURCE_DIR}/install-libs.sh"
    DEPENDS bakery_glue frender
    COMMENT "Installing libs and DllImport aliases into Assets/Plugins/x86_64")
endif()
