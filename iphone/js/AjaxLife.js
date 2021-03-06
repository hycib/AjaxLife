AjaxLife = {}
AjaxLife.ServerLookup = '/api/findserver'; // Set to false to disable the lookup and just use the APIRoot as it stands.
AjaxLife.ServerIP = null;
AjaxLife.SameOriginProxy = '/client/proxy.php/';
AjaxLife.APIRoot = '/api/'

// jQTouch init
AjaxLife.jqt = $.jQTouch({
    icon: 'images/icon.png',
    addGlossToIcon: false,
    startupScreen: 'images/splash.png',
    formSelector: 'form.jqt'
})

// This is called after a successful login.
AjaxLife.init = function() {
    AjaxLife.jqt.goTo('#mainmenu', 'flip');
    AjaxLife.MainMenu.init();
    AjaxLife.NameCache.init();
    AjaxLife.Nearby.init();
    AjaxLife.LocalChat.init();
    AjaxLife.Network.init();
}

AjaxLife.Debug = function(module, text) {
    if(window.console) {
        if(text) {
            console.log(module + ": " + text);
        } else {
            console.log(module);
        }
    }
}

$(document).ready(function() {
    AjaxLife.UI.init()
    AjaxLife.Login.init()
})