#include <QGuiApplication>
#include <QIcon>
#include <QQmlApplicationEngine>

int main(int argc, char *argv[])
{
    QGuiApplication app(argc, argv);
    app.setApplicationName(QStringLiteral("MC Profile Studio - EvolveUI Preview"));
    app.setWindowIcon(QIcon(QStringLiteral(":/qt/qml/MCProfileStudioEvolve/assets/app.ico")));

    QQmlApplicationEngine engine;
    QObject::connect(&engine, &QQmlApplicationEngine::objectCreationFailed,
                     &app, [] { QCoreApplication::exit(-1); }, Qt::QueuedConnection);
    engine.loadFromModule(QStringLiteral("MCProfileStudioEvolve"), QStringLiteral("Main"));
    return app.exec();
}
