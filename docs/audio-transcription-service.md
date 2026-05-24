# Audio Transcription Service

`AudioTranscriptionService` provides local transcription through `Whisper.net` `1.9.0`.

## Service Location

- `Services/AudioTranscriptionService.cs`

## Responsibility

This service is responsible for:

- checking whether the Whisper model is installed
- downloading the Whisper model when missing
- preparing input audio or video into mono `16 kHz` WAV for Whisper
- running Whisper transcription locally
- exposing transcript output as:
  - plain text
  - timestamped text
  - timestamped segments
  - timestamped words (synthesized from segment envelopes)

This service does not write subtitle files. Subtitle shaping, karaoke cue construction, styling, and file generation live in `SubtitlesService`.

## Public API

- `IsInstalled()`
- `InstallAsync(...)`
- `TranscribeToSegmentsAsync(...)`
- `TranscribeToWordsAsync(...)`
- `TranscribeToTextAsync(...)`
- `TranscribeToTimestampedTextAsync(...)`

## Timing

The service runs a single Whisper segment pass and returns `AudioTranscriptionSegment` values. Word-level output (`AudioTranscriptionWord`) is synthesized by splitting segment text on whitespace and distributing each segment's duration across its words proportional to character count. This avoids relying on Whisper.net token timestamps, which were observed to be unreliable for word-level alignment.

## Input Handling

- audio input is converted to Whisper-ready mono `16 kHz` WAV
- video input is audio-extracted first, denoised with the transcription prep profile, then converted to Whisper-ready mono `16 kHz` WAV
- temporary prepared files are cleaned up after transcription completes

## Progress

The service reports `AudioTranscriptionProgress` using these stages:

- `PreparingAudio`
- `Transcribing`
- `Completed`

When subtitle generation is requested by `SubtitlesService`, that service adds `WritingSubtitles` on top of the transcription progress stream.

## Related Service

- `Services/SubtitlesService.cs`

Use `SubtitlesService` when you need subtitle-oriented cue construction, styling, or file generation built on top of the raw Whisper timing output.
