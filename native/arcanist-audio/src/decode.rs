//! Decodes a media stream with Symphonia and feeds the analyzer.

use std::io::{self, Read, Seek, SeekFrom};

use symphonia::core::codecs::audio::AudioDecoderOptions;
use symphonia::core::errors::Error;
use symphonia::core::formats::probe::Hint;
use symphonia::core::formats::{FormatOptions, TrackType};
use symphonia::core::io::{MediaSource, MediaSourceStream};
use symphonia::core::meta::MetadataOptions;

use crate::analyzer::{Analysis, Analyzer};

/// Why decoding failed. `Unsupported` tells the caller to try another decoder (ffmpeg).
#[derive(Debug)]
pub enum DecodeFailure {
    Unsupported(String),
    Invalid(String),
}

/// Corrupt packets are skipped, as players do, unless the stream is mostly corrupt.
const MAX_DECODE_ERRORS: u32 = 50;

pub fn analyze<S: MediaSource + 'static>(source: S, extension: Option<&str>, flags: u32) -> Result<Analysis, DecodeFailure> {
    let stream = MediaSourceStream::new(Box::new(source), Default::default());
    let mut hint = Hint::new();
    if let Some(extension) = extension {
        hint.with_extension(extension);
    }

    let mut format = symphonia::default::get_probe()
        .probe(&hint, stream, FormatOptions::default(), MetadataOptions::default())
        .map_err(|e| DecodeFailure::Unsupported(e.to_string()))?;

    let track = format
        .default_track(TrackType::Audio)
        .ok_or_else(|| DecodeFailure::Unsupported("no audio track".into()))?;
    let track_id = track.id;
    let params = track
        .codec_params
        .as_ref()
        .and_then(|p| p.audio())
        .ok_or_else(|| DecodeFailure::Unsupported("no audio parameters".into()))?
        .clone();

    let mut decoder = symphonia::default::get_codecs()
        .make_audio_decoder(&params, &AudioDecoderOptions::default())
        .map_err(|e| DecodeFailure::Unsupported(e.to_string()))?;

    let mut analyzer: Option<Analyzer> = None;
    let mut samples: Vec<f32> = Vec::new();
    let mut errors = 0;

    loop {
        let packet = match format.next_packet() {
            Ok(Some(packet)) => packet,
            Ok(None) => break,
            // Truncated files end with an unexpected EOF: analyze what was decoded.
            Err(Error::IoError(e)) if e.kind() == io::ErrorKind::UnexpectedEof => break,
            Err(e) => return Err(DecodeFailure::Invalid(e.to_string())),
        };
        if packet.track_id != track_id {
            continue;
        }

        let buffer = match decoder.decode(&packet) {
            Ok(buffer) => buffer,
            Err(Error::DecodeError(_)) => {
                errors += 1;
                if errors > MAX_DECODE_ERRORS {
                    return Err(DecodeFailure::Invalid("too many decode errors".into()));
                }
                continue;
            }
            Err(e) => return Err(DecodeFailure::Invalid(e.to_string())),
        };

        let spec = buffer.spec();
        let (rate, channels) = (spec.rate(), spec.channels().count() as u32);
        let analyzer = match analyzer.as_mut() {
            Some(existing) => existing,
            None => analyzer.insert(Analyzer::new(rate, channels, flags).map_err(DecodeFailure::Invalid)?),
        };
        // A stream changing format midway (chained Ogg) is left to ffmpeg.
        if analyzer.format() != (rate, channels) {
            return Err(DecodeFailure::Unsupported("audio format changes within the stream".into()));
        }

        buffer.copy_to_vec_interleaved(&mut samples);
        analyzer.push(&samples).map_err(DecodeFailure::Invalid)?;
    }

    analyzer
        .ok_or_else(|| DecodeFailure::Invalid("no audio decoded".into()))?
        .finish()
        .map_err(DecodeFailure::Invalid)
}


/// A `MediaSource` over any seekable reader whose length is known.
pub struct ReaderSource<R> {
    reader: R,
    length: Option<u64>,
}

impl<R: Read + Seek + Send + Sync> ReaderSource<R> {
    pub fn new(reader: R, length: Option<u64>) -> Self {
        ReaderSource { reader, length }
    }
}

impl<R: Read> Read for ReaderSource<R> {
    fn read(&mut self, buf: &mut [u8]) -> io::Result<usize> {
        self.reader.read(buf)
    }
}

impl<R: Seek> Seek for ReaderSource<R> {
    fn seek(&mut self, pos: SeekFrom) -> io::Result<u64> {
        self.reader.seek(pos)
    }
}

impl<R: Read + Seek + Send + Sync> MediaSource for ReaderSource<R> {
    fn is_seekable(&self) -> bool {
        true
    }

    fn byte_len(&self) -> Option<u64> {
        self.length
    }
}
