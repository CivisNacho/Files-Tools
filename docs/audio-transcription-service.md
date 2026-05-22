# Audio Transcription Service

`AudioTranscriptionService` provides local transcription through `Whisper.net` `1.9.0`.

## Service Location

- `Services/AudioTranscriptionService.cs`

## Responsibility

This service is responsible for:

- checking whether the Whisper base model is installed
- downloading the Whisper base model when missing
- preparing input audio or video into mono `16 kHz` WAV for Whisper
- running Whisper transcription locally
- exposing transcript output as:
  - plain text
  - timestamped text
  - timestamped segments
  - timestamped words
  - detailed segment, token, and aligned-word results

This service does not write subtitle files. Subtitle shaping, karaoke cue construction, styling, and file generation live in `SubtitlesService`.

## Public API

- `IsInstalled()`
- `InstallAsync(...)`
- `TranscribeToSegmentsAsync(...)`
- `TranscribeToWordsAsync(...)`
- `TranscribeToDetailedResultAsync(...)`
- `TranscribeToTextAsync(...)`
- `TranscribeToTimestampedTextAsync(...)`

## Timing Modes

The service now supports two internal transcription granularities:

- segment mode:
  - used by plain transcript and timestamped segment callers
  - returns `AudioTranscriptionSegment`
- detailed mode:
  - used by karaoke and subtitle-oriented flows
  - returns `AudioTranscriptionDetailedResult`
  - includes `AudioTranscriptionDetailedSegment`, raw `AudioTranscriptionToken`, and cleaned `AudioTranscriptionAlignedWord`

## Detailed Timing Pipeline

The detailed path uses this timing pipeline:

1. run Whisper with normal segments plus token timestamps
2. capture `SegmentData.Text`, `Start`, `End`, `Probability`, `NoSpeechProbability`, `Language`, and raw `Tokens`
3. align raw tokens into words
4. clean word timing per source segment
5. expose the cleaned result to subtitle-oriented callers

The adapter intentionally does not use `SplitOnWord()` in the primary detailed pass because karaoke generation needs raw token boundaries first, not already-split word output.

For the detailed pass, the default `Whisper.net` adapter attempts to enable richer timing with:

- `WhisperFactoryOptions.UseDtwTimeStamps = true`
- `WithTokenTimestamps()`
- `WithTokenTimestampsThreshold(...)`
- `WithTokenTimestampsSumThreshold(...)`

## Fallback Chain

If raw token timing is unavailable or not usable for some non-empty segments, the service falls back in this order:

1. `AudioTranscriptionTimingSource.RawTokenAlignment`
2. `AudioTranscriptionTimingSource.WhisperWordTiming`
   - runs a secondary Whisper pass with `SplitOnWord()`
3. `AudioTranscriptionTimingSource.SegmentFallback`
   - synthesizes word timing from the source segment envelope

This keeps `TranscribeToWordsAsync(...)` stable for existing callers while letting karaoke consumers inspect the richer timing source and raw token payload.

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
