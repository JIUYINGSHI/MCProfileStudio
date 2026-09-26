import QtQuick
import QtQuick.Controls
import QtQuick.Layouts
import QtQuick.Effects
import "components" as Evolve

ApplicationWindow {
    id: root
    visible: true
    width: 1440
    height: 900
    minimumWidth: 1120
    minimumHeight: 720
    title: "MC Profile Studio · EvolveUI Preview"
    color: "#081018"

    property int currentPage: 0
    property color accent: "#38bdf8"
    property color cardColor: "#bf101b26"
    property color mutedText: "#a9bdce"

    Evolve.ETheme { id: theme; isDark: true; focusColor: root.accent }
    FontLoader { id: appFont; source: "qrc:/qt/qml/MCProfileStudioEvolve/assets/MapleMono.ttf" }
    FontLoader { id: iconFont; source: "qrc:/qt/qml/MCProfileStudioEvolve/assets/FontAwesome.otf" }

    font.family: appFont.name

    Image {
        id: background
        anchors.fill: parent
        source: "qrc:/qt/qml/MCProfileStudioEvolve/assets/background.jpg"
        fillMode: Image.PreserveAspectCrop
        opacity: 0.46
        layer.enabled: true
        layer.effect: MultiEffect { blurEnabled: true; blur: 0.22; blurMax: 32; saturation: 0.72 }
    }

    Rectangle { anchors.fill: parent; color: "#8f050b11" }

    RowLayout {
        anchors.fill: parent
        spacing: 0

        Rectangle {
            Layout.preferredWidth: 250
            Layout.fillHeight: true
            color: "#dc08121c"
            border.color: "#263d5265"

            ColumnLayout {
                anchors.fill: parent
                anchors.margins: 20
                spacing: 10

                RowLayout {
                    Layout.fillWidth: true
                    Layout.bottomMargin: 20
                    Rectangle {
                        width: 44; height: 44; radius: 12
                        gradient: Gradient { GradientStop { position: 0; color: "#38bdf8" } GradientStop { position: 1; color: "#2563eb" } }
                        Text { anchors.centerIn: parent; text: "◆"; color: "white"; font.pixelSize: 20 }
                    }
                    ColumnLayout {
                        Text { text: "MC PROFILE"; color: "white"; font.pixelSize: 20; font.bold: true }
                        Text { text: "EvolveUI 实验分支"; color: root.mutedText; font.pixelSize: 12 }
                    }
                }

                Repeater {
                    model: ["概览", "资源包", "光影包", "键位配置"]
                    delegate: Rectangle {
                        required property int index
                        required property string modelData
                        Layout.fillWidth: true
                        height: 52
                        radius: 16
                        color: root.currentPage === index ? "#3438bdf8" : navMouse.containsMouse ? "#182d4559" : "transparent"
                        border.color: root.currentPage === index ? "#6038bdf8" : "transparent"
                        Behavior on color { ColorAnimation { duration: 160 } }
                        Row {
                            anchors.verticalCenter: parent.verticalCenter
                            anchors.left: parent.left; anchors.leftMargin: 18
                            spacing: 13
                            Text { text: ["⌂", "▦", "◈", "⌨"][index]; color: root.currentPage === index ? root.accent : root.mutedText; font.pixelSize: 18 }
                            Text { text: modelData; color: root.currentPage === index ? "white" : root.mutedText; font.pixelSize: 15; font.bold: root.currentPage === index }
                        }
                        MouseArea { id: navMouse; anchors.fill: parent; hoverEnabled: true; cursorShape: Qt.PointingHandCursor; onClicked: root.currentPage = index }
                    }
                }

                Item { Layout.fillHeight: true }

                Rectangle {
                    Layout.fillWidth: true; height: 104; radius: 16; color: "#70121f2b"; border.color: "#304d6a80"
                    Column { anchors.fill: parent; anchors.margins: 14; spacing: 5
                        Text { text: "当前实例"; color: root.mutedText; font.pixelSize: 12 }
                        Text { width: parent.width; text: "NAST hard d0.9.9"; color: "white"; font.pixelSize: 14; font.bold: true; elide: Text.ElideRight }
                        Text { width: parent.width; text: "D:/Minecraft/.minecraft/versions/..."; color: "#7892a8"; font.pixelSize: 10; elide: Text.ElideMiddle }
                    }
                }
            }
        }

        Item {
            Layout.fillWidth: true
            Layout.fillHeight: true

            ColumnLayout {
                anchors.fill: parent
                anchors.margins: 28
                spacing: 18

                RowLayout {
                    Layout.fillWidth: true
                    ColumnLayout {
                        Layout.fillWidth: true
                        Text { text: ["概览", "资源包排序", "光影包覆盖", "可视化键位"][root.currentPage]; color: "white"; font.pixelSize: 32; font.bold: true }
                        Text { text: ["集中管理多个 Minecraft 整合包配置", "像游戏内一样管理启用状态与优先级", "为每个光影包维护效果预览", "按实体键定位占用、组合键与冲突"][root.currentPage]; color: root.mutedText; font.pixelSize: 13 }
                    }
                    Evolve.EButton { text: "导入游戏文件夹"; size: "s"; containerColor: "#168bd2"; hoverColor: "#24a7ef"; textColor: "white"; shadowEnabled: false }
                    Evolve.EButton { text: "应用到当前实例"; size: "s"; containerColor: "#2563eb"; hoverColor: "#3478ff"; textColor: "white"; shadowEnabled: false }
                }

                Loader {
                    Layout.fillWidth: true
                    Layout.fillHeight: true
                    sourceComponent: [overviewPage, packsPage, shadersPage, keysPage][root.currentPage]
                }
            }
        }
    }

    component GlassCard: Rectangle {
        radius: 22
        color: root.cardColor
        border.color: "#405b7185"
        border.width: 1
        layer.enabled: true
        layer.effect: MultiEffect { shadowEnabled: true; shadowColor: "#90000000"; shadowBlur: 0.7; shadowVerticalOffset: 8 }
    }

    Component {
        id: overviewPage
        GridLayout {
            columns: width > 900 ? 3 : 2
            columnSpacing: 16; rowSpacing: 16
            Repeater {
                model: [{n:"28", t:"资源包配置", s:"5 套已保存方案"}, {n:"14", t:"光影包", s:"自动匹配效果图"}, {n:"431", t:"键位", s:"3 处有效冲突"}, {n:"4", t:"游戏实例", s:"最近应用于 NAST hard"}]
                delegate: GlassCard {
                    required property var modelData
                    Layout.fillWidth: true; Layout.preferredHeight: 180
                    Column { anchors.fill: parent; anchors.margins: 24; spacing: 10
                        Text { text: modelData.n; color: root.accent; font.pixelSize: 42; font.bold: true }
                        Text { text: modelData.t; color: "white"; font.pixelSize: 18; font.bold: true }
                        Text { text: modelData.s; color: root.mutedText; font.pixelSize: 13 }
                    }
                }
            }
        }
    }

    Component {
        id: packsPage
        ColumnLayout {
            spacing: 14
            RowLayout {
                Layout.fillWidth: true
                Evolve.EDropdown { Layout.preferredWidth: 240; title: "默认资源包配置"; model: [{text:"默认资源包配置"}, {text:"科技包配置"}, {text:"轻量原版配置"}]; selectedIndex: 0; containerColor: "#df142331"; textColor: "white"; shadowEnabled: false }
                Item { Layout.fillWidth: true }
                Evolve.EButton { text: "新建配置"; size: "xs"; containerColor: "#26394a"; textColor: "white"; shadowEnabled: false }
                Evolve.EButton { text: "保存配置"; size: "xs"; containerColor: "#168bd2"; textColor: "white"; shadowEnabled: false }
            }
            RowLayout {
                Layout.fillWidth: true; Layout.fillHeight: true; spacing: 16
                PackColumn { Layout.fillWidth: true; title: "未应用的资源包"; items: ["进步牌匾装饰", "AE2 资源回溯", "Functional Storage", "传奇工具提示", "Create PBR 旧版适配"] }
                ColumnLayout {
                    Layout.preferredWidth: 112
                    Item { Layout.fillHeight: true }
                    Evolve.EButton { text: "全部启用 →"; size: "xs"; containerColor: "#168bd2"; textColor: "white"; shadowEnabled: false }
                    Evolve.EButton { text: "← 全部关闭"; size: "xs"; containerColor: "#34495c"; textColor: "white"; shadowEnabled: false }
                    Item { Layout.fillHeight: true }
                }
                PackColumn { Layout.fillWidth: true; title: "已应用（上方优先）"; items: ["HarmonyOS 字体包", "附魔描边 零零构想兼容", "Cym Create PBR 矿石发光", "五红豆 05RD", "Clean Connected Glass"] }
            }
        }
    }

    component PackColumn: GlassCard {
        required property string title
        required property var items
        ColumnLayout {
            anchors.fill: parent; anchors.margins: 18; spacing: 12
            Text { text: parent.parent.title; color: "white"; font.pixelSize: 19; font.bold: true }
            ListView {
                Layout.fillWidth: true; Layout.fillHeight: true; spacing: 9; clip: true
                model: parent.parent.items
                delegate: Rectangle {
                    required property string modelData
                    width: ListView.view.width; height: 82; radius: 15; color: itemMouse.containsMouse ? "#cc1b3041" : "#a512222f"
                    Behavior on color { ColorAnimation { duration: 140 } }
                    Rectangle { x: 12; anchors.verticalCenter: parent.verticalCenter; width: 58; height: 58; radius: 10; color: "#263e52"; Text { anchors.centerIn: parent; text: "◈"; color: root.accent; font.pixelSize: 22 } }
                    Column { x: 84; anchors.verticalCenter: parent.verticalCenter; width: parent.width - 104; spacing: 5
                        Text { width: parent.width; text: modelData; color: "white"; font.pixelSize: 14; font.bold: true; wrapMode: Text.Wrap }
                        Text { width: parent.width; text: "资源包说明与横幅预览将在此显示"; color: root.mutedText; font.pixelSize: 11; elide: Text.ElideRight }
                    }
                    MouseArea { id: itemMouse; anchors.fill: parent; hoverEnabled: true; cursorShape: Qt.PointingHandCursor }
                }
            }
        }
    }

    Component {
        id: shadersPage
        RowLayout {
            spacing: 16
            GlassCard {
                Layout.preferredWidth: 440; Layout.fillHeight: true
                ColumnLayout { anchors.fill: parent; anchors.margins: 18
                    Text { text: "光影包库"; color: "white"; font.pixelSize: 20; font.bold: true }
                    ListView { Layout.fillWidth: true; Layout.fillHeight: true; spacing: 9; clip: true; model: ["Complementary Reimagined", "Photon", "BSL v8.2", "Sundial", "Soft Voxels Lite"]
                        delegate: Rectangle { required property string modelData; width: ListView.view.width; height: 88; radius: 15; color: index === 0 ? "#293b82a8" : "#a512222f"
                            Rectangle { x: 12; anchors.verticalCenter: parent.verticalCenter; width: 112; height: 64; radius: 9; color: "#28445a"; Text { anchors.centerIn: parent; text: "效果图"; color: root.mutedText } }
                            Column { x: 140; anchors.verticalCenter: parent.verticalCenter; Text { text: modelData; color: "white"; font.bold: true; font.pixelSize: 14 } Text { text: "点击选择"; color: root.mutedText; font.pixelSize: 11 } }
                        }
                    }
                }
            }
            GlassCard { Layout.fillWidth: true; Layout.fillHeight: true
                ColumnLayout { anchors.fill: parent; anchors.margins: 22
                    Text { text: "Complementary Reimagined"; color: "white"; font.pixelSize: 22; font.bold: true }
                    Rectangle { Layout.fillWidth: true; Layout.fillHeight: true; radius: 18; color: "#263b4d"; Text { anchors.centerIn: parent; text: "网络效果图预览"; color: root.mutedText; font.pixelSize: 20 } }
                    RowLayout { Layout.fillWidth: true; Item { Layout.fillWidth: true } Evolve.EButton { text: "选择本地效果图"; size: "xs"; containerColor: "#34495c"; textColor: "white"; shadowEnabled: false } Evolve.EButton { text: "设为当前光影"; size: "xs"; containerColor: "#168bd2"; textColor: "white"; shadowEnabled: false } }
                }
            }
        }
    }

    Component {
        id: keysPage
        ColumnLayout {
            spacing: 14
            GlassCard { Layout.fillWidth: true; Layout.preferredHeight: 270
                ColumnLayout { anchors.fill: parent; anchors.margins: 18; spacing: 7
                    RowLayout { Layout.fillWidth: true; Text { text: "实体键位视图"; color: "white"; font.pixelSize: 19; font.bold: true } Item { Layout.fillWidth: true } Evolve.EDropdown { width: 180; title: "96 / 98 键"; model: [{text:"68 键"},{text:"87 键 TKL"},{text:"96 / 98 键"},{text:"108 键"}]; selectedIndex: 2; containerColor: "#df142331"; textColor: "white"; shadowEnabled: false } }
                    Repeater { model: ["ESC F1 F2 F3 F4 F5 F6 F7 F8 F9 F10 F11 F12 DEL", "` 1 2 3 4 5 6 7 8 9 0 - = BACKSPACE", "TAB Q W E R T Y U I O P [ ] \\", "CAPS A S D F G H J K L ; ' ENTER", "LSHIFT Z X C V B N M , . / RSHIFT", "LCTRL LWIN LALT SPACE RALT FN RCTRL"]
                        delegate: Row { required property string modelData; spacing: 5; anchors.horizontalCenter: parent.horizontalCenter
                            Repeater { model: modelData.split(" "); delegate: Rectangle { required property string modelData; width: modelData === "SPACE" ? 190 : modelData.length > 5 ? 82 : 48; height: 31; radius: 8; color: ["W","A","S","D","R","LSHIFT"].includes(modelData) ? "#c64755" : ["F2","TAB","Y","O","SPACE"].includes(modelData) ? "#087ecc" : "#34495c"; Text { anchors.centerIn: parent; text: modelData; color: "white"; font.pixelSize: modelData.length > 4 ? 8 : 11 } } }
                        }
                    }
                }
            }
            RowLayout { Layout.fillWidth: true; Layout.fillHeight: true; spacing: 16
                GlassCard { Layout.fillWidth: true; Layout.fillHeight: true
                    ColumnLayout { anchors.fill: parent; anchors.margins: 18
                        Text { text: "全部键位功能"; color: "white"; font.pixelSize: 18; font.bold: true }
                        ListView { Layout.fillWidth: true; Layout.fillHeight: true; spacing: 5; clip: true; model: ["前进  ·  Minecraft  ·  W", "打开背包  ·  Minecraft  ·  E", "终端  ·  Applied Energistics 2  ·  T", "喷气背包模式  ·  Mekanism  ·  G"]
                            delegate: Rectangle { required property string modelData; width: ListView.view.width; height: 52; radius: 11; color: index === 0 ? "#2538bdf8" : "#7a142331"; Text { anchors.fill: parent; anchors.margins: 14; text: modelData; color: "white"; verticalAlignment: Text.AlignVCenter } }
                        }
                    }
                }
                GlassCard { Layout.preferredWidth: 350; Layout.fillHeight: true
                    ColumnLayout { anchors.fill: parent; anchors.margins: 20; spacing: 12
                        Text { text: "键位编辑"; color: "white"; font.pixelSize: 19; font.bold: true }
                        Text { text: "前进 / key.forward"; color: "white"; font.pixelSize: 15 }
                        Text { text: "我的世界 / Minecraft"; color: root.accent; font.pixelSize: 12 }
                        Evolve.EButton { Layout.fillWidth: true; text: "当前：W（点击重新绑定）"; size: "xs"; containerColor: "#168bd2"; textColor: "white"; shadowEnabled: false }
                        CheckBox { text: "计入冲突检测"; checked: true; palette.windowText: "white" }
                        CheckBox { text: "记住为此 Mod 的专属键位"; checked: false; palette.windowText: "white" }
                        Item { Layout.fillHeight: true }
                        Text { text: "键位配置草稿"; color: root.mutedText; font.pixelSize: 12 }
                        Evolve.EDropdown { Layout.fillWidth: true; title: "默认键位"; model: [{text:"默认键位"},{text:"科技整合包"}]; selectedIndex: 0; containerColor: "#df142331"; textColor: "white"; shadowEnabled: false }
                    }
                }
            }
        }
    }
}
