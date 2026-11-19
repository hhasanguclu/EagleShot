fn main() {
    println!("cargo:rerun-if-changed=Resources/icon.ico");
    if std::env::var("CARGO_CFG_TARGET_OS").as_deref() == Ok("windows") {
        let mut res = winresource::WindowsResource::new();
        res.set_icon("Resources/icon.ico");
        res.set("ProductName", "EagleShot");
        res.set("FileDescription", "EagleShot");
        res.set("LegalCopyright", "MIT License");
        if let Err(e) = res.compile() {
            println!("cargo:warning=could not embed the icon: {e}");
        }
    }
}
