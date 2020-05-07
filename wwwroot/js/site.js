// Please see documentation at https://docs.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.
function setCookie(c_name, value, expiredays) {
    var exdate = new Date();
    exdate.setDate(exdate.getDate() + expiredays);
    document.cookie = c_name + "=" + value + ";path=/" + ((expiredays === null) ? "" : ";expires=" + exdate.toGMTString());
}
function getCookie(name) {
    var dc = document.cookie;
    var prefix = name + "=";
    var begin = dc.indexOf("; " + prefix);
    if (begin === -1) {
        begin = dc.indexOf(prefix);
        if (begin !== 0) return null;
    } else {
        begin += 2;
    }
    var end = document.cookie.indexOf(";", begin);
    if (end === -1) {
        end = dc.length;
    }
    return unescape(dc.substring(begin + prefix.length, end));
}
function saveinfo() {
    setCookie("personsname", document.getElementById("PersonsName").value, 2);
    setCookie("email", document.getElementById("Email").value, 2);
    setCookie("siteid", document.getElementById("SiteID").value, 2);

}
function getinfo() {
    var personsname = getCookie("personsname");
    var email = getCookie("email");
    var siteid = getCookie("siteid");
    document.getElementById("PersonsName").value = personsname;
    document.getElementById("Email").value = email;
    document.getElementById("SiteID").value = siteid;




}  


