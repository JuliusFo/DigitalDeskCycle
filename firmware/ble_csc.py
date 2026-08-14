"""
BLE peripheral implementing the "Cycling Speed and Cadence" profile
(CSC, service 0x1816).

Reports the cumulative number of crank revolutions. Because the value is
cumulative rather than event based, a lost notification has no consequence --
the next one carries the correct total. The receiver derives cadence itself
from two consecutive notifications.

Deliberately built against the raw `bluetooth` API instead of `aioble`: that one
is present in every Pico W build, needs no extra installation, and does not force
the existing, verified counting loop into an asyncio corset.

Usage:

    from ble_csc import CscPeripheral
    ble = CscPeripheral("DeskCycle")
    ...
    ble.notify(count, last_pulse_ms)     # per revolution and periodically
"""

import struct
import bluetooth
from micropython import const

# ---------------------------------------------------------------- BLE constants

_IRQ_CENTRAL_CONNECT = const(1)
_IRQ_CENTRAL_DISCONNECT = const(2)

_FLAG_READ = const(0x0002)
_FLAG_NOTIFY = const(0x0010)

_ADV_TYPE_FLAGS = const(0x01)
_ADV_TYPE_UUID16_COMPLETE = const(0x03)
_ADV_TYPE_NAME = const(0x09)
_ADV_TYPE_APPEARANCE = const(0x19)

_ADV_FLAGS_GENERAL_DISCOVERABLE = const(0x06)
_ADV_INTERVAL_US = const(250000)

# Appearance 0x0483 -- "Cycling: Cadence Sensor". Only controls the icon a
# fitness app shows next to the device.
_APPEARANCE_CADENCE_SENSOR = const(1155)

# CSC Feature: bit 1 = "Crank Revolution Data Supported". We provide no wheel
# data, only crank revolutions.
_CSC_FEATURE_CRANK_REV = const(0x0002)

# Sensor Location 5 = "Left Crank".
_SENSOR_LOCATION_LEFT_CRANK = const(5)

# Flags in the measurement packet: bit 0 = wheel data, bit 1 = crank data.
_MEASUREMENT_FLAGS_CRANK_ONLY = const(0x02)

_CSC_SERVICE = (
    bluetooth.UUID(0x1816),
    (
        (bluetooth.UUID(0x2A5B), _FLAG_NOTIFY),   # CSC Measurement
        (bluetooth.UUID(0x2A5C), _FLAG_READ),     # CSC Feature
        (bluetooth.UUID(0x2A5D), _FLAG_READ),     # Sensor Location
    ),
)


def _advertising_payload(name, service_uuid, appearance):
    """Assemble the advertising packet. At most 31 bytes; the four fields below
    take 22 with a nine-character name."""
    payload = bytearray()

    def _append(adv_type, value):
        payload.append(len(value) + 1)
        payload.append(adv_type)
        payload.extend(value)

    _append(_ADV_TYPE_FLAGS, struct.pack("B", _ADV_FLAGS_GENERAL_DISCOVERABLE))
    _append(_ADV_TYPE_UUID16_COMPLETE, bytes(service_uuid))
    _append(_ADV_TYPE_APPEARANCE, struct.pack("<H", appearance))
    _append(_ADV_TYPE_NAME, name.encode())

    if len(payload) > 31:
        raise ValueError("advertising packet too long (%d bytes) -- shorten the name" % len(payload))
    return payload


class CscPeripheral:

    def __init__(self, name="DeskCycle"):
        self._connections = set()

        self._ble = bluetooth.BLE()
        self._ble.active(True)
        self._ble.config(gap_name=name)
        self._ble.irq(self._on_ble_event)

        ((self._h_measurement, self._h_feature, self._h_location),) = \
            self._ble.gatts_register_services((_CSC_SERVICE,))

        self._ble.gatts_write(self._h_feature, struct.pack("<H", _CSC_FEATURE_CRANK_REV))
        self._ble.gatts_write(self._h_location, struct.pack("<B", _SENSOR_LOCATION_LEFT_CRANK))

        self._payload = _advertising_payload(
            name, _CSC_SERVICE[0], _APPEARANCE_CADENCE_SENSOR)
        self._advertise()

    def _advertise(self):
        self._ble.gap_advertise(_ADV_INTERVAL_US, adv_data=self._payload)

    def _on_ble_event(self, event, data):
        if event == _IRQ_CENTRAL_CONNECT:
            conn_handle, _, _ = data
            self._connections.add(conn_handle)
        elif event == _IRQ_CENTRAL_DISCONNECT:
            conn_handle, _, _ = data
            self._connections.discard(conn_handle)
            self._advertise()   # otherwise the device is invisible after disconnecting

    @property
    def connected(self):
        return len(self._connections) > 0

    def mac(self):
        try:
            _, addr = self._ble.config("mac")
            return ":".join("%02X" % b for b in addr)
        except Exception:
            return "?"

    def notify(self, revolutions, event_ms):
        """Send the current state to every connected receiver.

        `event_ms` is the timestamp of the last revolution in milliseconds
        (ticks_ms). The profile expects 1/1024 seconds as a 16-bit value, which
        rolls over every 64 seconds -- that is by design, receivers compute
        modulo. The revolution counter is 16 bit as well and rolls over after
        65535 revolutions.
        """
        if not self._connections:
            return

        event_time = (event_ms * 1024 // 1000) & 0xFFFF
        data = struct.pack("<BHH",
                           _MEASUREMENT_FLAGS_CRANK_ONLY,
                           revolutions & 0xFFFF,
                           event_time)

        for conn_handle in tuple(self._connections):
            try:
                self._ble.gatts_notify(conn_handle, self._h_measurement, data)
            except OSError:
                pass    # connection just dropped -- the disconnect event cleans up
