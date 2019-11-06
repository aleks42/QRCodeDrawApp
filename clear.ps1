function del_
{
    Param($Path)
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue "$Path"
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue "$Path"
}

del_ ".\QRCodeEncoder\bin\"
del_ ".\QRCodeEncoder\obj\"

del_ ".\QRCode\bin\"
del_ ".\QRCode\obj\"

del_ ".\QRCode.Android\bin\"
del_ ".\QRCode.Android\obj\"

del_ ".\QRCode.iOS\bin\"
del_ ".\QRCode.iOS\obj\"
