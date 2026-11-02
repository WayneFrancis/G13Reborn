// Experimental G13Reborn virtual keyboard (KMDF + Microsoft's VHF).
// Do not install this driver on a production machine until WDK build,
// Static Driver Verifier and isolated-machine testing are complete.
#include <ntddk.h>
#include <wdf.h>
#include <wdmsec.h>
#include <vhf.h>

#define G13VHF_MAGIC 0x33314856UL // "VH13", little endian
#define G13VHF_PROTOCOL_VERSION 1
#define G13VHF_REPORT_SIZE 8
#define IOCTL_G13VHF_KEYBOARD \
    CTL_CODE(FILE_DEVICE_UNKNOWN, 0x801, METHOD_BUFFERED, FILE_WRITE_DATA)

// Strict 16-byte contract shared with the C# probe client.
#pragma pack(push, 1)
typedef struct _G13VHF_KEYBOARD_COMMAND {
    ULONG Magic;
    USHORT Version;
    USHORT ReportLength;
    UCHAR Report[G13VHF_REPORT_SIZE];
} G13VHF_KEYBOARD_COMMAND, *PG13VHF_KEYBOARD_COMMAND;
#pragma pack(pop)

C_ASSERT(sizeof(G13VHF_KEYBOARD_COMMAND) == 16);

// HID boot-format keyboard report: modifier, reserved, 6 key usages.
// No REPORT_ID item; packet.reportId = 0, data length is 8.
// No LED output or vendor-defined feature handling in this initial probe.
static UCHAR g_KeyboardDescriptor[] = {
    0x05, 0x01,       // Usage Page (Generic Desktop)
    0x09, 0x06,       // Usage (Keyboard)
    0xA1, 0x01,       // Collection (Application)
    0x05, 0x07,       // Usage Page (Keyboard/Keypad)
    0x19, 0xE0,       // Usage Minimum (Left Control)
    0x29, 0xE7,       // Usage Maximum (Right GUI)
    0x15, 0x00,       // Logical Minimum (0)
    0x25, 0x01,       // Logical Maximum (1)
    0x75, 0x01,       // Report Size (1)
    0x95, 0x08,       // Report Count (8)
    0x81, 0x02,       // Input (Data, Var, Abs): modifiers
    0x95, 0x01,       // Report Count (1)
    0x75, 0x08,       // Report Size (8)
    0x81, 0x03,       // Input (Const, Var, Abs): reserved
    0x95, 0x06,       // Report Count (6)
    0x75, 0x08,       // Report Size (8)
    0x15, 0x00,       // Logical Minimum (0)
    0x25, 0x65,       // Logical Maximum (101)
    0x05, 0x07,       // Usage Page (Keyboard/Keypad)
    0x19, 0x00,       // Usage Minimum (0)
    0x29, 0x65,       // Usage Maximum (101)
    0x81, 0x00,       // Input (Data, Array, Abs): six keys
    0xC0              // End Collection
};

typedef struct _G13VHF_DEVICE_CONTEXT {
    VHFHANDLE VhfHandle;
} G13VHF_DEVICE_CONTEXT, *PG13VHF_DEVICE_CONTEXT;

WDF_DECLARE_CONTEXT_TYPE_WITH_NAME(G13VHF_DEVICE_CONTEXT, G13VhfGetContext);

DRIVER_INITIALIZE DriverEntry;
EVT_WDF_DRIVER_DEVICE_ADD G13VhfEvtDeviceAdd;
EVT_WDF_IO_QUEUE_IO_DEVICE_CONTROL G13VhfEvtIoDeviceControl;
EVT_WDF_OBJECT_CONTEXT_CLEANUP G13VhfEvtDeviceCleanup;
EVT_WDF_FILE_CLEANUP G13VhfEvtFileCleanup;

static NTSTATUS G13VhfSubmit(
    _In_ PG13VHF_DEVICE_CONTEXT ctx,
    _In_reads_(G13VHF_REPORT_SIZE) const UCHAR* report)
{
    UCHAR copy[G13VHF_REPORT_SIZE];
    HID_XFER_PACKET packet;

    if (ctx->VhfHandle == NULL)
        return STATUS_DEVICE_NOT_READY;

    RtlCopyMemory(copy, report, sizeof(copy));
    RtlZeroMemory(&packet, sizeof(packet));
    packet.reportBuffer = copy;
    packet.reportBufferLen = sizeof(copy);
    packet.reportId = 0;
    // VHF is responsible for buffering, and the stack buffer can be
    // reused after VhfReadReportSubmit returns.
    return VhfReadReportSubmit(ctx->VhfHandle, &packet);
}

static VOID G13VhfSubmitAllReleased(_In_ PG13VHF_DEVICE_CONTEXT ctx)
{
    const UCHAR released[G13VHF_REPORT_SIZE] = { 0 };
    (VOID)G13VhfSubmit(ctx, released);
}

// If the control application exits or crashes, release every held key.
VOID G13VhfEvtFileCleanup(_In_ WDFFILEOBJECT fileObject)
{
    WDFDEVICE device = WdfFileObjectGetDevice(fileObject);
    G13VhfSubmitAllReleased(G13VhfGetContext(device));
}

VOID G13VhfEvtDeviceCleanup(_In_ WDFOBJECT deviceObject)
{
    PG13VHF_DEVICE_CONTEXT ctx =
        G13VhfGetContext((WDFDEVICE)deviceObject);
    if (ctx->VhfHandle != NULL)
    {
        VhfDelete(ctx->VhfHandle, TRUE);
        ctx->VhfHandle = NULL;
    }
}

VOID G13VhfEvtIoDeviceControl(
    _In_ WDFQUEUE queue,
    _In_ WDFREQUEST request,
    _In_ size_t outputBufferLength,
    _In_ size_t inputBufferLength,
    _In_ ULONG ioControlCode)
{
    PG13VHF_DEVICE_CONTEXT ctx =
        G13VhfGetContext(WdfIoQueueGetDevice(queue));
    PG13VHF_KEYBOARD_COMMAND command = NULL;
    size_t size = 0;
    NTSTATUS status;

    UNREFERENCED_PARAMETER(outputBufferLength);
    UNREFERENCED_PARAMETER(inputBufferLength);

    if (ioControlCode != IOCTL_G13VHF_KEYBOARD)
    {
        WdfRequestComplete(request, STATUS_INVALID_DEVICE_REQUEST);
        return;
    }

    status = WdfRequestRetrieveInputBuffer(
        request, sizeof(G13VHF_KEYBOARD_COMMAND),
        (PVOID*)&command, &size);
    if (!NT_SUCCESS(status))
    {
        WdfRequestComplete(request, status);
        return;
    }

    if (size != sizeof(G13VHF_KEYBOARD_COMMAND) ||
        command->Magic != G13VHF_MAGIC ||
        command->Version != G13VHF_PROTOCOL_VERSION ||
        command->ReportLength != G13VHF_REPORT_SIZE ||
        command->Report[1] != 0)
    {
        WdfRequestComplete(request, STATUS_INVALID_PARAMETER);
        return;
    }

    // USB HID keyboard array usages 0-101 only for this descriptor.
    // Reject unsupported values rather than feeding malformed data to HIDClass.
    for (ULONG i = 2; i < G13VHF_REPORT_SIZE; i++)
    {
        if (command->Report[i] > 0x65)
        {
            WdfRequestComplete(request, STATUS_INVALID_PARAMETER);
            return;
        }
    }

    status = G13VhfSubmit(ctx, command->Report);
    WdfRequestComplete(request, status);
}

NTSTATUS G13VhfEvtDeviceAdd(
    _In_ WDFDRIVER driver,
    _Inout_ PWDFDEVICE_INIT deviceInit)
{
    NTSTATUS status;
    WDFDEVICE device;
    WDF_OBJECT_ATTRIBUTES attributes;
    WDF_IO_QUEUE_CONFIG queueConfig;
    WDF_FILEOBJECT_CONFIG fileConfig;
    PG13VHF_DEVICE_CONTEXT ctx;
    VHF_CONFIG config;
    UNICODE_STRING name;
    UNICODE_STRING link;

    UNREFERENCED_PARAMETER(driver);

    // Unique, administrator-only control path; no unauthenticated
    // application may send system-wide keyboard input.
    WdfDeviceInitSetDeviceType(deviceInit, FILE_DEVICE_UNKNOWN);
    WdfDeviceInitSetExclusive(deviceInit, TRUE);
    RtlInitUnicodeString(&name, L"\\Device\\G13RebornVhid");
    status = WdfDeviceInitAssignName(deviceInit, &name);
    if (!NT_SUCCESS(status))
        return status;

    status = WdfDeviceInitAssignSDDLString(
        deviceInit, &SDDL_DEVOBJ_SYS_ALL_ADM_ALL);
    if (!NT_SUCCESS(status))
        return status;

    WDF_FILEOBJECT_CONFIG_INIT(&fileConfig,
        WDF_NO_EVENT_CALLBACK, WDF_NO_EVENT_CALLBACK,
        G13VhfEvtFileCleanup);
    WdfDeviceInitSetFileObjectConfig(
        deviceInit, &fileConfig, WDF_NO_OBJECT_ATTRIBUTES);

    WDF_OBJECT_ATTRIBUTES_INIT_CONTEXT_TYPE(
        &attributes, G13VHF_DEVICE_CONTEXT);
    attributes.EvtCleanupCallback = G13VhfEvtDeviceCleanup;
    status = WdfDeviceCreate(&deviceInit, &attributes, &device);
    if (!NT_SUCCESS(status))
        return status;

    ctx = G13VhfGetContext(device);
    ctx->VhfHandle = NULL;

    // Only the separate elevated probe can access this named link.
    RtlInitUnicodeString(&link, L"\\DosDevices\\G13RebornVhid");
    status = WdfDeviceCreateSymbolicLink(device, &link);
    if (!NT_SUCCESS(status))
        return status;

    WDF_IO_QUEUE_CONFIG_INIT_DEFAULT_QUEUE(
        &queueConfig, WdfIoQueueDispatchSequential);
    queueConfig.EvtIoDeviceControl = G13VhfEvtIoDeviceControl;
    status = WdfIoQueueCreate(device, &queueConfig,
        WDF_NO_OBJECT_ATTRIBUTES, WDF_NO_HANDLE);
    if (!NT_SUCCESS(status))
        return status;

    VHF_CONFIG_INIT(&config, WdfDeviceWdmGetDeviceObject(device),
        sizeof(g_KeyboardDescriptor), g_KeyboardDescriptor);
    // Experimental synthetic identifiers. NEVER impersonate Logitech.
    config.VendorID = 0xF055;
    config.ProductID = 0x0013;
    config.VersionNumber = 1;

    status = VhfCreate(&config, &ctx->VhfHandle);
    if (!NT_SUCCESS(status))
        return status;

    return VhfStart(ctx->VhfHandle);
}

NTSTATUS DriverEntry(
    _In_ PDRIVER_OBJECT driverObject,
    _In_ PUNICODE_STRING registryPath)
{
    WDF_DRIVER_CONFIG config;
    WDF_DRIVER_CONFIG_INIT(&config, G13VhfEvtDeviceAdd);
    return WdfDriverCreate(driverObject, registryPath,
        WDF_NO_OBJECT_ATTRIBUTES, &config, WDF_NO_HANDLE);
}
