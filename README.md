# Endorphin.Core

This project provides infrastructure for the development of scientific instrument control software. It was developed by the University
of Warwick EPR group where it is used to run a confocal microscope and a high field EPR spectrometer. We are in the process of migrating
our instrument control projects to open source. In the process, we will first migrate the infrastructure projects to GitHub and consume
them via NuGet, then the projects for individual instruments, before finally open-sourcing experiments which integrate several instruments.

The Endorphin.Core project includes various utility functions and a module for wrapping instruments with C APIs into asynchronous
message-processing agents. Endorphin.Core.NationalInstruments includes a module for wrapping NI VISA instruments in a similar manner but
depends on the National Instruments VISA 14.0.1 drivers which cannot be redistributed but are freely available. In order to build/use
Endorphin.Core.NationalInstruments, download [NI VISA 14.0.1][1] (and [NI 488.2 14.0][2] if you also require support for GPIB devices) and
ensure that you select .NET Framework 4.5 Language Support during installation (under NI-VISA 14.0.1 -> Development Support) during
installation on both pacakges.

Documentation: http://anton-pt.github.io/Endorphin.Core
More documentation to follow...

## Maintainer(s)

- [@anton-pt](https://github.com/anton-pt)

 [1]: http://www.ni.com/download/ni-visa-14.0.1/5023/en/
 [2]: http://www.ni.com/download/ni-488.2-14.0/4802/en/