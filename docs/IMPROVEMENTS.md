# Improvements

## Block A — Project foundation

## Block B — Model management

## Block C — Inference and Real-ESRGAN end-to-end flow

## Block D — Editor: comparison, history and quality modes

## Block E — Face restoration and background removal

## Block F — Delivery: README, benchmarks and installers

## Block G — Phase 2: denoising

## Block H — Editor: basic edits and toolbar

### §PB67 Crop

The owner asked for crop on 2026-10-23. Pressing Crop (or C) turns the photo surface
into a crop editor: a rectangle over the whole photo with eight handles, dragged to
resize and from inside to move, the area outside it dimmed. A small bar over the photo
offers the aspect: Free, Original, 1:1, 4:3, 3:2 and 16:9, each also flipped for
portrait; it shows the rectangle's size in the photo's own pixels (so a crop before a 4x
upscale can be aimed at a final size), and Apply and Cancel, with Enter and Esc. The
rectangle snaps to whole pixels and never leaves the photo. Apply copies those pixels
into a new version, with no resampling; zoom and pan keep working while cropping. The
surface is PhotoBoost.Render's GPU surface, so the rectangle and dimming are drawn there
and the handles are hit-tested in photo coordinates; the surface publishes the rectangle
through its automation status, so a UI case can set it and read it back.

### §PB66 Flip horizontal and vertical

The owner asked for basic edits on 2026-10-23. Flip horizontal mirrors the photo left to
right, and Flip vertical top to bottom. As with rotating (PB65), both are exact moves of
the 16-bit samples in PhotoBoost.Imaging, alpha included and with no resampling, so
flipping twice gives back the same bytes, which a unit test checks. Each flip is a
version in the history (PB28), labelled "Flip horizontal" or "Flip vertical", and can be
undone and compared. A UI case flips the tiny fixture photo and reads the history and
the image size back.

### §PB68 Invert colours

The owner listed "crop, invert, flip" on 2026-10-23. Flip is PB66, so invert is read
here as inverting the colours: each RGB sample becomes its complement, alpha kept, which
turns a scanned colour or black-and-white negative into a positive. It is an exact
operation on the 16-bit samples, so inverting twice gives back the same bytes, and it is
a version in the history like the other edits. Left as an idea until the owner says
whether "invert" meant this or mirroring, which PB66 already covers; if it meant
mirroring, this line is retired.
