cmake_minimum_required(VERSION 3.21)

project(MCProfileStudioEvolve VERSION 0.1 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

find_package(Qt6 6.8 REQUIRED COMPONENTS Quick QuickControls2)
qt_standard_project_setup(REQUIRES 6.8)

qt_add_executable(MCProfileStudioEvolve WIN32 main.cpp)

set(EVOLVE_ROOT "${CMAKE_CURRENT_SOURCE_DIR}/../third_party/EvolveUI")
set(EVOLVE_COMPONENT_NAMES ETheme.qml EButton.qml EBlurCard.qml EInput.qml EDropdown.qml)
set(EVOLVE_COMPONENTS)
file(MAKE_DIRECTORY "${CMAKE_CURRENT_BINARY_DIR}/generated/components")
foreach(component_name IN LISTS EVOLVE_COMPONENT_NAMES)
    set(component "${CMAKE_CURRENT_BINARY_DIR}/generated/components/${component_name}")
    configure_file("${EVOLVE_ROOT}/components/${component_name}" "${component}" COPYONLY)
    set_source_files_properties("${component}" PROPERTIES
        GENERATED TRUE
        QT_RESOURCE_ALIAS "components/${component_name}"
    )
    list(APPEND EVOLVE_COMPONENTS "${component}")
endforeach()

set_source_files_properties(qml/Main.qml PROPERTIES QT_RESOURCE_ALIAS Main.qml)
set_source_files_properties(../Assets/background.jpg PROPERTIES QT_RESOURCE_ALIAS assets/background.jpg)
set_source_files_properties(../Assets/app.ico PROPERTIES QT_RESOURCE_ALIAS assets/app.ico)
set_source_files_properties(../Assets/MapleMono.ttf PROPERTIES QT_RESOURCE_ALIAS assets/MapleMono.ttf)
set_source_files_properties("${EVOLVE_ROOT}/fonts/fontawesome-free-6.7.2-desktop/otfs/Font Awesome 6 Free-Solid-900.otf" PROPERTIES QT_RESOURCE_ALIAS assets/FontAwesome.otf)

qt_add_qml_module(MCProfileStudioEvolve
    URI MCProfileStudioEvolve
    VERSION 1.0
    QML_FILES qml/Main.qml ${EVOLVE_COMPONENTS}
    RESOURCES
        ../Assets/background.jpg
        ../Assets/app.ico
        ../Assets/MapleMono.ttf
        "${EVOLVE_ROOT}/fonts/fontawesome-free-6.7.2-desktop/otfs/Font Awesome 6 Free-Solid-900.otf"
)

target_link_libraries(MCProfileStudioEvolve PRIVATE Qt6::Quick Qt6::QuickControls2)

set_target_properties(MCProfileStudioEvolve PROPERTIES
    WIN32_EXECUTABLE TRUE
    OUTPUT_NAME "MCProfileStudio-EvolveUI"
)

install(TARGETS MCProfileStudioEvolve RUNTIME DESTINATION .)
