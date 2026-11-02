# G13Reborn experimental virtual HID keyboard

**Source-stage prototype only; NOT tested against a live kernel, not signed,
and not safe to install on the user's primary PC yet.** This is deliberately
separate from the functioning G13Reborn C# input-forwarding path.

This root-enumerated KMDF HID source driver calls Microsoft's VHF to create
one virtual keyboard HID collection, and exposes an **administrator-only**
control path \`\\.\G13RebornVhid\`. A user-mode test client sends a
versioned 16-byte IOCTL packet containing an 8-byte keyboard HID report.
No kernel-mode keyboard filters, physical G13 hardware overrides, Logitech
driver modifications, or legacy bus tricks are used.

Sources:
- https://learn.microsoft.com/en-us/windows-hardware/drivers/hid/virtual-hid-framework--vhf-
- https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/vhf/nf-vhf-vhfreadreportsubmit
- https://learn.microsoft.com/en-us/windows-hardware/drivers/wdf/controlling-device-access-in-kmdf-drivers

## Safety boundaries

- An administrator must explicitly install and run the probe. The IOCTL
  requires FILE_WRITE_DATA and the parent driver control device has the
  SYSTEM + administrators-only SDDL. Exclusive opening prevents two clients
  sending conflicting reports.
- Malformed, oversized, version-mismatched, or unsupported keyboard reports
  are rejected before VHF submission.
- Closing the control device (including process crash) submits an empty
  keyboard report to release keys.
- The descriptor is a basic 6-key-rollover keyboard. It implements no media
  keys, mouse, consumer controls or output features.
- The source, the preliminary INF, and even a successful .sys build **do not imply an installable or signed
  driver**. Do not run \`pnputil\`, enable test-signing, disable Secure Boot
  or change kernel configuration based solely on this prototype.
- The production .NET solution continues to use the existing \`SendInput\`
  backend unchanged.

## Development sequence (isolated test VM)

1. Install a compatible Windows SDK and WDK with KMDF tooling on an
   isolated test VM; do not modify the primary gaming PC.
2. Build \`G13RebornVhid.vcxproj\` for x64 in Visual Studio with WDK.
   Review warnings, INF validation, link symbols and Static Driver Verifier
   output; fix any failures before considering installation.
3. Test signing and root-device installation must be handled through
   established Microsoft driver-package and VM deployment workflows.
   Do not copy, sign, or install drivers without explicit review.
4. Run the .NET \`G13Reborn.VhfProbe\` client with \`--probe\` first, then
   manually opt into \`--test-k\` while observing the Raw Input diagnostic.
5. Confirm a **new** HID device (NOT Logitech PID C232) provides the K
   key-down/up events. Test disconnect/kill, duplicate key and app-exit
   releases before connecting any real G13 mappings.

## Wire contract

On the privileged \`\\.\G13RebornVhid\` path, send
\`CTL_CODE(FILE_DEVICE_UNKNOWN,0x801,METHOD_BUFFERED,FILE_WRITE_DATA)\`
(\`0x22A004\`) with 16 bytes:

| Offset | Field | Value |
| --- | --- | --- |
| 0 | Magic, little-endian UINT32 | 0x33314856 ("VH13") |
| 4 | Version, little-endian UINT16 | 1 |
| 6 | Keyboard report length UINT16 | 8 |
| 8 | Report[0] | USB HID modifier bits |
| 9 | Report[1] | reserved, must equal 0 |
| 10-15 | Report[2..7] | up to 6 USB HID keyboard usages (0..101) |

The client and driver agree on this contract; a smoke test verifies the
exact serialized packet bytes. USB HID usage 0x0E is the letter K on a
standard keyboard layout. Submitting all zero bytes releases all keys.

## Native CI build stage

The separate `.github/workflows/vhf-driver.yml` job restores Microsoft's
Windows 11 WDK and SDK NuGet packages (version 10.0.28000.2526)
and compiles the KMDF/VHF `.sys` code on a Windows 2025 runner with
Visual Studio 2026/MSBuild 18.

The INF remains a **draft**, included as a non-build item
because the hosted WDK environment did not expose the `stampinf.exe`
packaging tool. The native CI job builds the driver, then calls
`Validate-Package.ps1` to stage the unsigned `.inf` and `.sys` in an
ephemeral directory. The script checks a few essential source-level
declarations and attempts **official** `InfVerif /w /v` and
`Inf2Cat /nocat` checks for Windows 11 24H2 and 25H2 x64.

**Read the CI job summary for each result.** A tool reported as
`NOT RUN` means the hosted runner lacked it; a green job alone
does **not** mean that Microsoft validated the INF or package.
If a tool is found, a nonzero validation exit code fails the job.
A developer with a full WDK installation can require both tools:

```powershell
# After compiling the native x64 Release driver on an isolated dev VM:
./drivers/G13RebornVhid/Validate-Package.ps1 -RequireWdkTools
```

This step **does not** generate a catalog, sign any driver,
install a kernel component, invoke `pnputil`, or exercise input against
a live OS. The staged files are not published as an installable
artifact. INF validation and signability must pass before a separate,
reviewed VM-only driver installation test.

References:
- https://learn.microsoft.com/en-us/windows-hardware/drivers/devtest/infverif
- https://learn.microsoft.com/en-us/windows-hardware/drivers/devtest/inf2cat
