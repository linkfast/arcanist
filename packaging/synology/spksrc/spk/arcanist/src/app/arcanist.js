// Arcanist's window in DSM: a DSM application whose window shows index.html (the package's state and settings).
// The page gets the session's SynoToken, which DSM asks for (with the session cookie) to tell who is signed in.
Ext.ns("SYNOCOMMUNITY.Arcanist");

SYNOCOMMUNITY.Arcanist.AppInstance = Ext.extend(SYNO.SDS.AppInstance, {
    appWindowName: "SYNOCOMMUNITY.Arcanist.AppWindow",
    constructor: function () {
        SYNOCOMMUNITY.Arcanist.AppInstance.superclass.constructor.apply(this, arguments);
    }
});

SYNOCOMMUNITY.Arcanist.AppWindow = Ext.extend(SYNO.SDS.AppWindow, {
    constructor: function (config) {
        var token = (SYNO.SDS.Session && SYNO.SDS.Session.SynoToken) || "";
        config = Ext.apply({
            resizable: true,
            maximizable: true,
            minimizable: true,
            width: 780,
            height: 640,
            minWidth: 480,
            minHeight: 360,
            layout: "fit",
            border: false,
            items: [{
                xtype: "box",
                autoEl: {
                    tag: "iframe",
                    src: "/webman/3rdparty/arcanist/index.html?SynoToken=" + encodeURIComponent(token),
                    frameborder: 0,
                    style: "width: 100%; height: 100%; border: 0; background: #fff"
                }
            }]
        }, config);
        SYNOCOMMUNITY.Arcanist.AppWindow.superclass.constructor.call(this, config);
    }
});
