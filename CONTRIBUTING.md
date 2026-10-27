# Contributing

Thanks for helping! Issues and pull requests in English or German are both fine.

## Reporting hardware results

The most valuable contribution right now is testing on other machines, especially **Linux**.
Please open an issue with:

- OS and version, CPU, GPU, mainboard
- your water block / LCD model and its USB ID (`lsusb` or Device Manager)
- the output of `ttlcd list`
- what the LCD shows during `ttlcd demo` (a photo helps)

## Development

1. Install the [.NET 10 SDK](https://dotnet.microsoft.com/download).
2. `dotnet build` and `dotnet test` must pass.
3. Try your change on real hardware if you can: `dotnet run --project src/TtLcd -- list`.

Guidelines:

- Keep the protocol code in `Protocol/` free of I/O so it stays unit-testable; add a test for
  every encoding change.
- New sensor backends implement `ISensorSource` and report units as °C, %, W, MHz, V and RPM.
- Sensor IDs end up in users' config files — don't change existing IDs without a good reason.
- Match the existing code style (`.editorconfig` defaults, nullable enabled, no warnings).
- Never copy code from the official Thermaltake software. Protocol findings are welcome;
  document them in [docs/PROTOCOL.md](docs/PROTOCOL.md).

## Pull requests

- One topic per pull request, with a short description of what and why.
- Mention which hardware/OS you tested on.
