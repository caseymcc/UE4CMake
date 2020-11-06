cmake_minimum_required(VERSION 3.10)

macro(_ue4_get_target_link_libraries addTarget target_file_list target_linker_file_list)
    message(STATUS "targets: ${ARGN}")
    message(STATUS "get_target - addTarget: ${addTarget}  target_file_list: ${target_file_list}  target_linker_file_list: ${target_linker_file_list}")

    foreach(target ${ARGN})
        message(STATUS "target: ${target}")
        if(TARGET ${target})
            get_target_property(target_libraries ${target} INTERFACE_LINK_LIBRARIES)
            message(STATUS "target_libraries: ${target_libraries}")

            if(target_libraries) 
                #get info for targets libs
                _ue4_get_target_link_libraries(TRUE ${target_file_list} ${target_linker_file_list} ${target_libraries})
            endif()

            message(STATUS "addTarget: ${addTarget}")
            if(${addTarget})
                get_target_property(target_type ${target} TYPE)
                message(STATUS "target type: ${target_type}")
                if(target_type STREQUAL "SHARED_LIBRARY")
                    list(APPEND ${target_file_list} "\"$<TARGET_FILE:${target}>\"$<COMMA>")
                endif()
                if(target_type STREQUAL "STATIC_LIBRARY"
                    OR target_type STREQUAL "SHARED_LIBRARY")
                    message(STATUS "adding: ${target}")
                    list(APPEND ${target_linker_file_list} "$<TARGET_LINKER_FILE:${target}>")
                endif ()
            else()
                message(STATUS "not adding")
            endif()
        else()
            #not a target, need to hunt for library to include
            #currently ignored, use targets instead of files
            message(STATUS "not a target")
        endif()
    endforeach()

    if(${target_file_list})
        list(REMOVE_DUPLICATES ${target_file_list})
    endif()
    if(${target_linker_file_list})
        list(REMOVE_DUPLICATES ${target_linker_file_list})
    endif()
endmacro()

macro(ue4_get_target_link_libraries target_file_list target_linker_file_list)
    _ue4_get_target_link_libraries(TRUE ${target_file_list} ${target_linker_file_list} ${ARGN})
endmacro()

macro(target_build_info build_info target)
    message(STATUS "target_build_info - build_info=${build_info}")
    list(APPEND ${build_info} "includes=$<JOIN:$<REMOVE_DUPLICATES:$<TARGET_PROPERTY:worldgen,INCLUDE_DIRECTORIES>>,$<COMMA>>")

    ue4_get_target_link_libraries(target_binary_libs target_link_lib_files ${target})

    string(REPLACE ";" "$<SEMICOLON>" target_binary_libs "${target_binary_libs}")
    string(REPLACE ";" "$<SEMICOLON>" target_link_lib_files "${target_link_lib_files}")

    message(STATUS "target_binary_libs=${target_binary_libs}")
    message(STATUS "target_link_lib_files=${target_link_lib_files}")

    list(APPEND ${build_info} "binaries=$<JOIN:${target_binary_libs},$<COMMA>>")
    list(APPEND ${build_info} "libraries=$<JOIN:${target_link_lib_files},$<COMMA>>")
endmacro()

set(@BUILD_TARGET_NAME@_build_info "")

add_subdirectory(@BUILD_TARGET_DIR@ "@BUILD_TARGET_THIRDPARTY_DIR@/@BUILD_TARGET_NAME@/@BUILD_TARGET_BUILD_DIR@/@BUILD_TARGET_NAME@")
target_build_info("@BUILD_TARGET_NAME@_build_info" @BUILD_TARGET_NAME@)

project(@BUILD_TARGET_NAME@_build)

string(REPLACE ";" "\n" @BUILD_TARGET_NAME@_build_info "${@BUILD_TARGET_NAME@_build_info}")
message(STATUS "build_info=${@BUILD_TARGET_NAME@_build_info}")
file(GENERATE OUTPUT "@BUILD_TARGET_THIRDPARTY_DIR@/@BUILD_TARGET_NAME@/@BUILD_TARGET_BUILD_DIR@/buildinfo_$<CONFIG>.output" CONTENT "${@BUILD_TARGET_NAME@_build_info}")
