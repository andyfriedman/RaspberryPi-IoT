
$(function () {
    var chkFrontPatioSwitch = $("#front-patio-switch");
    var chkRearPatioSwitch = $("#rear-patio-switch");
    var chkGarageSwitch = $("#garage-switch");
    var chkDev = $("#dev");
    
    function publishLightState(lightSwitch, lightState) {
        var dev = chkDev.prop("checked") ? "&dev=true" : "";

        $.ajax({
            type: "PUT",
            url: "/api/lights/" + lightSwitch + "?command=" + lightState + dev,
            async: true,
            cache: false
        })
        .success(function (data, status) {
            console.log("Lights " + lightState + " request sent: " + status);
        })
        .error(function (e) {
            var msg = "Error: " + e.status + " " + e.statusText + " " + e.responseText;
            console.error(msg);
        });
    }

    chkFrontPatioSwitch.change(function () {
        var state = chkFrontPatioSwitch.prop("checked") ? "on" : "off";
        publishLightState("patio-front", state);
    });

    chkRearPatioSwitch.change(function () {
        var state = chkRearPatioSwitch.prop("checked") ? "on" : "off";
        publishLightState("patio-rear", state);
    });

    chkGarageSwitch.change(function () {
        var state = chkGarageSwitch.prop("checked") ? "on" : "off";
        publishLightState("garage", state);
    });
});