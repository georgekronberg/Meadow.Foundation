﻿using Meadow.Hardware;
using Meadow.Units;

namespace Meadow.Foundation.ICs.IOExpanders;

/// <summary>
/// Represents an Sc16is752 I/O expander device.
/// </summary>
public class Sc16is752 : Sc16is7x2
{
    /// <summary>
    /// Initializes a new instance of the Sc16is752 class using an oscillator frequency and an optional IRQ digital interrupt port.
    /// </summary>
    /// <param name="oscillatorFrequency">The oscillator frequency of the device.</param>
    /// <param name="irq">An optional digital interrupt port for IRQ (Interrupt Request).</param>
    public Sc16is752(Frequency oscillatorFrequency, IDigitalInterruptPort? irq) : base(oscillatorFrequency, irq)
    {
    }

    /// <summary>
    /// Initializes a new instance of the Sc16is752 class using an I2C bus, oscillator frequency, an address, and an optional IRQ digital interrupt port.
    /// </summary>
    /// <param name="i2cBus">The I2C bus for communication.</param>
    /// <param name="oscillatorFrequency">The oscillator frequency of the device.</param>
    /// <param name="address">The I2C address of the device (default is 0x48).</param>
    /// <param name="irq">An optional digital interrupt port for IRQ (Interrupt Request).</param>
    public Sc16is752(II2cBus i2cBus, Frequency oscillatorFrequency, Addresses address = Addresses.Address_0x48, IDigitalInterruptPort? irq = null) : base(i2cBus, oscillatorFrequency, address, irq)
    {
    }

    /// <summary>
    /// Initializes a new instance of the Sc16is752 class using an SPI bus, oscillator frequency, an optional chip select digital output port, and an optional IRQ digital interrupt port.
    /// </summary>
    /// <param name="spiBus">The SPI bus for communication.</param>
    /// <param name="oscillatorFrequency">The oscillator frequency of the device.</param>
    /// <param name="chipSelect">An optional digital output port for chip select (CS).</param>
    /// <param name="irq">An optional digital interrupt port for IRQ (Interrupt Request).</param>
    public Sc16is752(ISpiBus spiBus, Frequency oscillatorFrequency, IDigitalOutputPort? chipSelect = null, IDigitalInterruptPort? irq = null) : base(spiBus, oscillatorFrequency, chipSelect, irq)
    {
    }
}
