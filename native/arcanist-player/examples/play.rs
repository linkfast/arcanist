//! Lists the output devices, or plays files on one of them, for trying the player by hand.
//!
//!     cargo run --release --example play                          # the devices
//!     cargo run --release --example play -- <files…>              # plays them, one after another, on the default
//!     ARCANIST_DEVICE="<name>" ARCANIST_VOLUME=0.3 cargo run --release --example play -- <files…>

use std::fs::File;
use std::time::Duration;

use arcanist_player::{Item, devices, output};

fn main() {
    let files: Vec<String> = std::env::args().skip(1).collect();
    if files.is_empty() {
        for device in devices().expect("the devices") {
            println!("{}{} ({:?} Hz, {:?} channels)", if device.is_default { "* " } else { "  " }, device.name, device.sample_rate, device.channels);
        }
        return;
    }

    let (out, engine) = output::open(std::env::var("ARCANIST_DEVICE").ok(), 500).expect("the device opens");
    println!("Playing on {} at {} Hz, {} channels", out.device, engine.shared().format.rate, engine.shared().format.channels);
    let volume = std::env::var("ARCANIST_VOLUME").ok().and_then(|v| v.parse().ok()).unwrap_or(0.5);
    engine.set_volume(volume, false);
    for (index, path) in files.iter().enumerate() {
        let extension = path.rsplit('.').next().map(str::to_owned);
        let item = Item { source: Box::new(File::open(path).expect("the file opens")), extension, token: index as u64, start_ms: 0, end_ms: 0 };
        if index == 0 {
            engine.play(item, 0);
        } else {
            engine.queue(item);
        }
    }
    loop {
        std::thread::sleep(Duration::from_millis(500));
        let status = engine.status();
        println!("{} {:?} {} ms", status.state, status.token, status.position_ms);
        for error in &status.errors {
            println!("  {} failed: {}", error.token, error.error);
        }
        if status.state == "idle" {
            break;
        }
    }
}
