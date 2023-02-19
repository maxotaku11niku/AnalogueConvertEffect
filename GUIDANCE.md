# Guidance

AnalogueConvertEffect has a few settings, and it may not be immediately obvious what they do. First of all, try playing about with them for a bit and see what comes out. You can go a long way to understanding what each setting does just by trying it out! But if you really want to know, here's what they do.

## Format

Determines the video format to use, which can dramatically affect the output image. This effect supports all the 3 major formats that were used in the era of analogue TV:

- [PAL](https://en.wikipedia.org/wiki/PAL) (Phase Alternating Line): Developed in Europe, uses 576 video lines, the [YUV](https://en.wikipedia.org/wiki/YUV) colourspace and [quadrature amplitude modulation](https://en.wikipedia.org/wiki/Quadrature_amplitude_modulation) for the chroma subcarrier. Its defining feature is the alternation of the subcarrier phase with each scanline, which cancels out hue shifts associated with global phase errors.
- [NTSC](https://en.wikipedia.org/wiki/NTSC) (National Television System Committee): Developed in America, uses 480 video lines, the [YIQ](https://en.wikipedia.org/wiki/YIQ) colourspace and [quadrature amplitude modulation](https://en.wikipedia.org/wiki/Quadrature_amplitude_modulation) for the chroma subcarrier. Its defining feature is the fact that the Q (violet-green) component is transmitted at a lower bandwidth than the I (blue-orange) component.
- [SECAM](https://en.wikipedia.org/wiki/SECAM) (SÉquentiel de Couleur À Mémoire):  Developed in France, uses 576 video lines, the [YDbDr](https://en.wikipedia.org/wiki/YDbDr) colourspace and [frequency modulation](https://en.wikipedia.org/wiki/Frequency_modulation) for the chroma subcarrier. Its defining feature is the use of FM for the subcarrier, but this feature has proven very difficult for me to decode easily. Results from this format may not be very good.

## Do Interlacing?

Interlacing is drawing only alternate scanlines in any given field, a technique intended to fake a doubled framerate. The effect on a still image is mostly to do with the effective quality of the image. Disabling this will halve the horizontal resolution.

## Noise Amount

Adds white noise to the signal.

## Phase Noise

Simulates noise in the phase of the subcarrier decoder. This won't affect the colours of SECAM.

## Scanline Jitter

Simulates noise in the scanning mechanism.

## Crosstalk

Affects how much the chroma and luma information mix.

## Resonance

Affects how sharp the frequency cutoffs are in the filters.

## Phase Error

Affects the global phase error. This will cause the hue to shift in NTSC, and the saturation to shift in PAL. Does not affect the output of SECAM.

## Distortion Ramp

Affects the sigmoid distortion of the signal: higher values will lead to higher contrast.

## Output Channels

Determines which channels get output. Best to leave this one on the default setting.

- Y: The luminosity component. If left out it is set to a certain grey level.
- U: The 'blue-yellow' component. If left out it is set to zero. This corresponds to U in PAL, Q in NTSC and Db in SECAM.
- V: The 'red-cyan' component. If left out it is set to zero. This corresponds to V in PAL, I in NTSC and Dr in SECAM.