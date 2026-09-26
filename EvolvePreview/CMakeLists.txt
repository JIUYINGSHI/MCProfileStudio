cmake_minimum_required(VERSION 3.21)

project(MCProfileStudioEvolve VERSION 0.1 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

find_package(Qt6 6.8 REQUIRED COMPONENTS Quick QuickControls2)
qt_standard_project_setup(REQUIRES 6.8)

qt_add_executable(MCProfileStudioEvolve WIN32 main.cpp)

set(EVOLVE_ROOT "${CMAKE_CURRENT_SOURCE_DIR}/../third_party/EvolveUI")
set(EVOLVE_COMPONENTS
    "${EVOLVE_ROOT}/components/ETheme.qml"
    "${EVOLVE_ROOT}/components/EButton.qml"
    "${EVOLVE_ROOT}/components/EBlurCard.qml"
    "${EVOLVE_ROOT}/components/EInput.qml"
    "${EVOLVE_ROOT}/components/EDropdown.qml"
)

foreach(component IN LISTS EVOLVE_COMPONENTS)
    get_filename_component(component_name "${component}" NAME)
    set_source_files_properties("${component}" PROPERTIES QT_RESOURCE_ALIAS "components/${component_name}")
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
