namespace Silverdash360.Services
{
    /// <summary>
    /// The Xbox Dashboard's lists for Console Settings (dash 2.0.17559): its time
    /// zones (0x9295C440) with what each writes to user settings 1 to 7, its
    /// locales (0x92021720) with the country XamSetCountry takes, and its
    /// language lists by game region, in the order it shows them.
    /// </summary>
    internal static class ConsoleSettingsTables
    {
        internal sealed class Zone
        {
            public string Name;
            /// <summary>Minutes added to local time for UTC (setting 1).</summary>
            public int Bias;
            /// <summary>The first four characters of the standard and daylight names (settings 2 and 3).</summary>
            public uint StandardName, DaylightName;
            /// <summary>When standard and daylight time start: month, week (5 the last), day of the week, hour (settings 4 and 5).</summary>
            public uint StandardDate, DaylightDate;
            /// <summary>Settings 6 and 7.</summary>
            public int StandardBias, DaylightBias;
            public bool UsesDaylightSaving;
        }

        internal sealed class Language
        {
            public string Name;
            /// <summary>User setting 9.</summary>
            public uint Value;
            /// <summary>Xbox setting 7, the keyboard, for the input method variants; 0 for none.</summary>
            public ushort Keyboard;
        }

        public static readonly Zone[] Zones =
        {
            new Zone { Name = "GMT-12 Tokelau", Bias = 720, StandardName = 0x49444C57, DaylightName = 0x00000000, StandardDate = 0x00000000, DaylightDate = 0x00000000, StandardBias = 0, DaylightBias = 0, UsesDaylightSaving = false },
            new Zone { Name = "GMT-11 Samoa", Bias = 660, StandardName = 0x4E540000, DaylightName = 0x00000000, StandardDate = 0x00000000, DaylightDate = 0x00000000, StandardBias = 0, DaylightBias = 0, UsesDaylightSaving = false },
            new Zone { Name = "GMT-10 Hawaii", Bias = 600, StandardName = 0x48535400, DaylightName = 0x00000000, StandardDate = 0x00000000, DaylightDate = 0x00000000, StandardBias = 0, DaylightBias = 0, UsesDaylightSaving = false },
            new Zone { Name = "GMT-09 Alaska", Bias = 540, StandardName = 0x59535400, DaylightName = 0x59445400, StandardDate = 0x0B010002, DaylightDate = 0x03020002, StandardBias = 0, DaylightBias = -60, UsesDaylightSaving = true },
            new Zone { Name = "GMT-08 Pacific (U.S. & Canada)", Bias = 480, StandardName = 0x50535400, DaylightName = 0x50445400, StandardDate = 0x0B010002, DaylightDate = 0x03020002, StandardBias = 0, DaylightBias = -60, UsesDaylightSaving = true },
            new Zone { Name = "GMT-08 Baja California", Bias = 480, StandardName = 0x5053544D, DaylightName = 0x5044544D, StandardDate = 0x0A050002, DaylightDate = 0x04010002, StandardBias = 0, DaylightBias = -60, UsesDaylightSaving = true },
            new Zone { Name = "GMT-07 Arizona", Bias = 420, StandardName = 0x414D5354, DaylightName = 0x00000000, StandardDate = 0x00000000, DaylightDate = 0x00000000, StandardBias = 0, DaylightBias = 0, UsesDaylightSaving = false },
            new Zone { Name = "GMT-07 Mountain (U.S. & Canada)", Bias = 420, StandardName = 0x4D535400, DaylightName = 0x4D445400, StandardDate = 0x0B010002, DaylightDate = 0x03020002, StandardBias = 0, DaylightBias = -60, UsesDaylightSaving = true },
            new Zone { Name = "GMT-07 Chihuahua", Bias = 420, StandardName = 0x4D53544D, DaylightName = 0x4D44544D, StandardDate = 0x0A050002, DaylightDate = 0x04010002, StandardBias = 0, DaylightBias = -60, UsesDaylightSaving = true },
            new Zone { Name = "GMT-06 Central America", Bias = 360, StandardName = 0x43415354, DaylightName = 0x00000000, StandardDate = 0x00000000, DaylightDate = 0x00000000, StandardBias = 0, DaylightBias = 0, UsesDaylightSaving = false },
            new Zone { Name = "GMT-06 Central (U.S. & Canada)", Bias = 360, StandardName = 0x43535400, DaylightName = 0x43445400, StandardDate = 0x0B010002, DaylightDate = 0x03020002, StandardBias = 0, DaylightBias = -60, UsesDaylightSaving = true },
            new Zone { Name = "GMT-06 Mexico City", Bias = 360, StandardName = 0x4D535400, DaylightName = 0x4D445400, StandardDate = 0x0A050002, DaylightDate = 0x04010002, StandardBias = 0, DaylightBias = -60, UsesDaylightSaving = true },
            new Zone { Name = "GMT-06 Saskatchewan", Bias = 360, StandardName = 0x43435354, DaylightName = 0x00000000, StandardDate = 0x00000000, DaylightDate = 0x00000000, StandardBias = 0, DaylightBias = 0, UsesDaylightSaving = false },
            new Zone { Name = "GMT-05 Eastern (U.S. & Canada)", Bias = 300, StandardName = 0x45535400, DaylightName = 0x45445400, StandardDate = 0x0B010002, DaylightDate = 0x03020002, StandardBias = 0, DaylightBias = -60, UsesDaylightSaving = true },
            new Zone { Name = "GMT-05 Bogota", Bias = 300, StandardName = 0x53505354, DaylightName = 0x00000000, StandardDate = 0x00000000, DaylightDate = 0x00000000, StandardBias = 0, DaylightBias = 0, UsesDaylightSaving = false },
            new Zone { Name = "GMT-04:30 Caracas", Bias = 270, StandardName = 0x53575354, DaylightName = 0x00000000, StandardDate = 0x00000000, DaylightDate = 0x00000000, StandardBias = 0, DaylightBias = 0, UsesDaylightSaving = false },
            new Zone { Name = "GMT-04 Atlantic (U.S. & Canada)", Bias = 240, StandardName = 0x41535400, DaylightName = 0x41445400, StandardDate = 0x0B010002, DaylightDate = 0x03020002, StandardBias = 0, DaylightBias = -60, UsesDaylightSaving = true },
            new Zone { Name = "GMT-04 Santiago", Bias = 240, StandardName = 0x50535354, DaylightName = 0x50534454, StandardDate = 0x03020600, DaylightDate = 0x0A020600, StandardBias = 0, DaylightBias = -60, UsesDaylightSaving = true },
            new Zone { Name = "GMT-03:30 Newfoundland", Bias = 210, StandardName = 0x4E535400, DaylightName = 0x4E445400, StandardDate = 0x0B010002, DaylightDate = 0x03020002, StandardBias = 0, DaylightBias = -60, UsesDaylightSaving = true },
            new Zone { Name = "GMT-03 Brasilia", Bias = 180, StandardName = 0x45535354, DaylightName = 0x45534454, StandardDate = 0x02020600, DaylightDate = 0x0A030600, StandardBias = 0, DaylightBias = -60, UsesDaylightSaving = true },
            new Zone { Name = "GMT-03 Buenos Aires", Bias = 180, StandardName = 0x53455354, DaylightName = 0x53454454, StandardDate = 0x03020600, DaylightDate = 0x0A030600, StandardBias = 0, DaylightBias = -60, UsesDaylightSaving = true },
            new Zone { Name = "GMT-03 Greenland", Bias = 180, StandardName = 0x47535400, DaylightName = 0x47445400, StandardDate = 0x0A050002, DaylightDate = 0x04010002, StandardBias = 0, DaylightBias = -60, UsesDaylightSaving = true },
            new Zone { Name = "GMT-02 Mid-Atlantic", Bias = 120, StandardName = 0x4D415354, DaylightName = 0x4D414454, StandardDate = 0x09050002, DaylightDate = 0x03050002, StandardBias = 0, DaylightBias = -60, UsesDaylightSaving = true },
            new Zone { Name = "GMT-01 Azores", Bias = 60, StandardName = 0x41535400, DaylightName = 0x41445400, StandardDate = 0x0A050003, DaylightDate = 0x03050002, StandardBias = 0, DaylightBias = -60, UsesDaylightSaving = true },
            new Zone { Name = "GMT-01 Cape Verde Is.", Bias = 60, StandardName = 0x57415400, DaylightName = 0x00000000, StandardDate = 0x00000000, DaylightDate = 0x00000000, StandardBias = 0, DaylightBias = 0, UsesDaylightSaving = false },
            new Zone { Name = "GMT+00 Casablanca", Bias = 0, StandardName = 0x47535400, DaylightName = 0x00000000, StandardDate = 0x00000000, DaylightDate = 0x00000000, StandardBias = 0, DaylightBias = 0, UsesDaylightSaving = false },
            new Zone { Name = "GMT+00 London", Bias = 0, StandardName = 0x474D5400, DaylightName = 0x42535400, StandardDate = 0x0A050002, DaylightDate = 0x03050001, StandardBias = 0, DaylightBias = -60, UsesDaylightSaving = true },
            new Zone { Name = "GMT+01 Berlin", Bias = -60, StandardName = 0x57455354, DaylightName = 0x57454454, StandardDate = 0x0A050003, DaylightDate = 0x03050002, StandardBias = 0, DaylightBias = -60, UsesDaylightSaving = true },
            new Zone { Name = "GMT+01 Belgrade", Bias = -60, StandardName = 0x43455354, DaylightName = 0x43454454, StandardDate = 0x0A050003, DaylightDate = 0x03050002, StandardBias = 0, DaylightBias = -60, UsesDaylightSaving = true },
            new Zone { Name = "GMT+01 Paris, Madrid", Bias = -60, StandardName = 0x52535400, DaylightName = 0x52445400, StandardDate = 0x0A050003, DaylightDate = 0x03050002, StandardBias = 0, DaylightBias = -60, UsesDaylightSaving = true },
            new Zone { Name = "GMT+01 Sarajevo", Bias = -60, StandardName = 0x53435354, DaylightName = 0x53434454, StandardDate = 0x0A050003, DaylightDate = 0x03050002, StandardBias = 0, DaylightBias = -60, UsesDaylightSaving = true },
            new Zone { Name = "GMT+01 W. Central Africa", Bias = -60, StandardName = 0x57415354, DaylightName = 0x00000000, StandardDate = 0x00000000, DaylightDate = 0x00000000, StandardBias = 0, DaylightBias = 0, UsesDaylightSaving = false },
            new Zone { Name = "GMT+02 Athens", Bias = -120, StandardName = 0x47545354, DaylightName = 0x47544454, StandardDate = 0x0A050003, DaylightDate = 0x03050002, StandardBias = 0, DaylightBias = -60, UsesDaylightSaving = true },
            new Zone { Name = "GMT+02 Bucharest", Bias = -120, StandardName = 0x45455354, DaylightName = 0x45454454, StandardDate = 0x09050001, DaylightDate = 0x03050000, StandardBias = 0, DaylightBias = -60, UsesDaylightSaving = true },
            new Zone { Name = "GMT+02 Cairo", Bias = -120, StandardName = 0x45535400, DaylightName = 0x45445400, StandardDate = 0x09050302, DaylightDate = 0x05010502, StandardBias = 0, DaylightBias = -60, UsesDaylightSaving = true },
            new Zone { Name = "GMT+02 Pretoria", Bias = -120, StandardName = 0x53415354, DaylightName = 0x00000000, StandardDate = 0x00000000, DaylightDate = 0x00000000, StandardBias = 0, DaylightBias = 0, UsesDaylightSaving = false },
            new Zone { Name = "GMT+02 Helsinki", Bias = -120, StandardName = 0x464C5354, DaylightName = 0x464C4454, StandardDate = 0x0A050004, DaylightDate = 0x03050003, StandardBias = 0, DaylightBias = -60, UsesDaylightSaving = true },
            new Zone { Name = "GMT+02 Kaliningrad", Bias = -120, StandardName = 0x55535A31, DaylightName = 0x00000000, StandardDate = 0x00000000, DaylightDate = 0x00000000, StandardBias = 0, DaylightBias = 0, UsesDaylightSaving = false },
            new Zone { Name = "GMT+02 Jerusalem", Bias = -120, StandardName = 0x4A535400, DaylightName = 0x4A445400, StandardDate = 0x09050002, DaylightDate = 0x03050502, StandardBias = 0, DaylightBias = -60, UsesDaylightSaving = true },
            new Zone { Name = "GMT+03 Baghdad", Bias = -180, StandardName = 0x41535400, DaylightName = 0x41445400, StandardDate = 0x0A010004, DaylightDate = 0x04010003, StandardBias = 0, DaylightBias = -60, UsesDaylightSaving = true },
            new Zone { Name = "GMT+03 Kuwait", Bias = -180, StandardName = 0x41535400, DaylightName = 0x00000000, StandardDate = 0x00000000, DaylightDate = 0x00000000, StandardBias = 0, DaylightBias = 0, UsesDaylightSaving = false },
            new Zone { Name = "GMT+03 Nairobi", Bias = -180, StandardName = 0x45415354, DaylightName = 0x00000000, StandardDate = 0x00000000, DaylightDate = 0x00000000, StandardBias = 0, DaylightBias = 0, UsesDaylightSaving = false },
            new Zone { Name = "GMT+03 Moscow", Bias = -180, StandardName = 0x4D534B00, DaylightName = 0x00000000, StandardDate = 0x00000000, DaylightDate = 0x00000000, StandardBias = 0, DaylightBias = 0, UsesDaylightSaving = false },
            new Zone { Name = "GMT+03:30 Tehran", Bias = -210, StandardName = 0x49535400, DaylightName = 0x49445400, StandardDate = 0x09030100, DaylightDate = 0x03030600, StandardBias = 0, DaylightBias = -60, UsesDaylightSaving = true },
            new Zone { Name = "GMT+04 Abu Dhabi", Bias = -240, StandardName = 0x41535400, DaylightName = 0x00000000, StandardDate = 0x00000000, DaylightDate = 0x00000000, StandardBias = 0, DaylightBias = 0, UsesDaylightSaving = false },
            new Zone { Name = "GMT+04 Baku", Bias = -240, StandardName = 0x43535400, DaylightName = 0x43445400, StandardDate = 0x0A050003, DaylightDate = 0x03050002, StandardBias = 0, DaylightBias = -60, UsesDaylightSaving = true },
            new Zone { Name = "GMT+04 Samara", Bias = -240, StandardName = 0x53414D54, DaylightName = 0x00000000, StandardDate = 0x00000000, DaylightDate = 0x00000000, StandardBias = 0, DaylightBias = 0, UsesDaylightSaving = false },
            new Zone { Name = "GMT+04:30 Kabul", Bias = -270, StandardName = 0x41535400, DaylightName = 0x00000000, StandardDate = 0x00000000, DaylightDate = 0x00000000, StandardBias = 0, DaylightBias = 0, UsesDaylightSaving = false },
            new Zone { Name = "GMT+05 Islamabad", Bias = -300, StandardName = 0x57415354, DaylightName = 0x00000000, StandardDate = 0x00000000, DaylightDate = 0x00000000, StandardBias = 0, DaylightBias = 0, UsesDaylightSaving = false },
            new Zone { Name = "GMT+05 Ekaterinburg", Bias = -300, StandardName = 0x454B5354, DaylightName = 0x00000000, StandardDate = 0x00000000, DaylightDate = 0x00000000, StandardBias = 0, DaylightBias = 0, UsesDaylightSaving = false },
            new Zone { Name = "GMT+05:30 New Delhi", Bias = -330, StandardName = 0x49535400, DaylightName = 0x00000000, StandardDate = 0x00000000, DaylightDate = 0x00000000, StandardBias = 0, DaylightBias = 0, UsesDaylightSaving = false },
            new Zone { Name = "GMT+05:45 Kathmandu", Bias = -345, StandardName = 0x4E535400, DaylightName = 0x00000000, StandardDate = 0x00000000, DaylightDate = 0x00000000, StandardBias = 0, DaylightBias = 0, UsesDaylightSaving = false },
            new Zone { Name = "GMT+06 Almaty", Bias = -360, StandardName = 0x4E435354, DaylightName = 0x4E434454, StandardDate = 0x0A050003, DaylightDate = 0x03050002, StandardBias = 0, DaylightBias = -60, UsesDaylightSaving = true },
            new Zone { Name = "GMT+06 Dhaka", Bias = -360, StandardName = 0x43415354, DaylightName = 0x00000000, StandardDate = 0x00000000, DaylightDate = 0x00000000, StandardBias = 0, DaylightBias = 0, UsesDaylightSaving = false },
            new Zone { Name = "GMT+06 Sri Lanka", Bias = -360, StandardName = 0x53525354, DaylightName = 0x00000000, StandardDate = 0x00000000, DaylightDate = 0x00000000, StandardBias = 0, DaylightBias = 0, UsesDaylightSaving = false },
            new Zone { Name = "GMT+06 Omsk", Bias = -360, StandardName = 0x4F4D5354, DaylightName = 0x00000000, StandardDate = 0x00000000, DaylightDate = 0x00000000, StandardBias = 0, DaylightBias = 0, UsesDaylightSaving = false },
            new Zone { Name = "GMT+06:30 Yangon", Bias = -390, StandardName = 0x4D535400, DaylightName = 0x00000000, StandardDate = 0x00000000, DaylightDate = 0x00000000, StandardBias = 0, DaylightBias = 0, UsesDaylightSaving = false },
            new Zone { Name = "GMT+07 Bangkok", Bias = -420, StandardName = 0x53415354, DaylightName = 0x00000000, StandardDate = 0x00000000, DaylightDate = 0x00000000, StandardBias = 0, DaylightBias = 0, UsesDaylightSaving = false },
            new Zone { Name = "GMT+07 Krasnoyarsk", Bias = -420, StandardName = 0x4E415354, DaylightName = 0x00000000, StandardDate = 0x00000000, DaylightDate = 0x00000000, StandardBias = 0, DaylightBias = 0, UsesDaylightSaving = false },
            new Zone { Name = "GMT+08 Beijing", Bias = -480, StandardName = 0x43535400, DaylightName = 0x00000000, StandardDate = 0x00000000, DaylightDate = 0x00000000, StandardBias = 0, DaylightBias = 0, UsesDaylightSaving = false },
            new Zone { Name = "GMT+08 Hong Kong SAR", Bias = -480, StandardName = 0x484B5354, DaylightName = 0x00000000, StandardDate = 0x00000000, DaylightDate = 0x00000000, StandardBias = 0, DaylightBias = 0, UsesDaylightSaving = false },
            new Zone { Name = "GMT+08 Singapore", Bias = -480, StandardName = 0x4D505354, DaylightName = 0x00000000, StandardDate = 0x00000000, DaylightDate = 0x00000000, StandardBias = 0, DaylightBias = 0, UsesDaylightSaving = false },
            new Zone { Name = "GMT+08 Perth", Bias = -480, StandardName = 0x41575354, DaylightName = 0x00000000, StandardDate = 0x00000000, DaylightDate = 0x00000000, StandardBias = 0, DaylightBias = 0, UsesDaylightSaving = false },
            new Zone { Name = "GMT+08 Taipei", Bias = -480, StandardName = 0x54535400, DaylightName = 0x00000000, StandardDate = 0x00000000, DaylightDate = 0x00000000, StandardBias = 0, DaylightBias = 0, UsesDaylightSaving = false },
            new Zone { Name = "GMT+08 Irkutsk", Bias = -480, StandardName = 0x4E455354, DaylightName = 0x00000000, StandardDate = 0x00000000, DaylightDate = 0x00000000, StandardBias = 0, DaylightBias = 0, UsesDaylightSaving = false },
            new Zone { Name = "GMT+09 Tokyo", Bias = -540, StandardName = 0x54535400, DaylightName = 0x00000000, StandardDate = 0x00000000, DaylightDate = 0x00000000, StandardBias = 0, DaylightBias = 0, UsesDaylightSaving = false },
            new Zone { Name = "GMT+09 Seoul", Bias = -540, StandardName = 0x4B535400, DaylightName = 0x00000000, StandardDate = 0x00000000, DaylightDate = 0x00000000, StandardBias = 0, DaylightBias = 0, UsesDaylightSaving = false },
            new Zone { Name = "GMT+09 Yakutsk", Bias = -540, StandardName = 0x594B5354, DaylightName = 0x00000000, StandardDate = 0x00000000, DaylightDate = 0x00000000, StandardBias = 0, DaylightBias = 0, UsesDaylightSaving = false },
            new Zone { Name = "GMT+09:30 Adelaide", Bias = -570, StandardName = 0x41435354, DaylightName = 0x41434454, StandardDate = 0x04010003, DaylightDate = 0x0A010002, StandardBias = 0, DaylightBias = -60, UsesDaylightSaving = true },
            new Zone { Name = "GMT+09:30 Darwin", Bias = -570, StandardName = 0x41435354, DaylightName = 0x00000000, StandardDate = 0x00000000, DaylightDate = 0x00000000, StandardBias = 0, DaylightBias = 0, UsesDaylightSaving = false },
            new Zone { Name = "GMT+10 Brisbane", Bias = -600, StandardName = 0x41455354, DaylightName = 0x00000000, StandardDate = 0x00000000, DaylightDate = 0x00000000, StandardBias = 0, DaylightBias = 0, UsesDaylightSaving = false },
            new Zone { Name = "GMT+10 Sydney", Bias = -600, StandardName = 0x41455354, DaylightName = 0x41454454, StandardDate = 0x04010003, DaylightDate = 0x0A010002, StandardBias = 0, DaylightBias = -60, UsesDaylightSaving = true },
            new Zone { Name = "GMT+10 Guam", Bias = -600, StandardName = 0x57505354, DaylightName = 0x00000000, StandardDate = 0x00000000, DaylightDate = 0x00000000, StandardBias = 0, DaylightBias = 0, UsesDaylightSaving = false },
            new Zone { Name = "GMT+10 Hobart", Bias = -600, StandardName = 0x54535400, DaylightName = 0x54445400, StandardDate = 0x04010003, DaylightDate = 0x0A010002, StandardBias = 0, DaylightBias = -60, UsesDaylightSaving = true },
            new Zone { Name = "GMT+10 Vladivostok", Bias = -600, StandardName = 0x56535400, DaylightName = 0x00000000, StandardDate = 0x00000000, DaylightDate = 0x00000000, StandardBias = 0, DaylightBias = 0, UsesDaylightSaving = false },
            new Zone { Name = "GMT+11 Srednekolymsk", Bias = -660, StandardName = 0x53524554, DaylightName = 0x00000000, StandardDate = 0x00000000, DaylightDate = 0x00000000, StandardBias = 0, DaylightBias = 0, UsesDaylightSaving = false },
            new Zone { Name = "GMT+11 Solomon Islands", Bias = -660, StandardName = 0x43505354, DaylightName = 0x00000000, StandardDate = 0x00000000, DaylightDate = 0x00000000, StandardBias = 0, DaylightBias = 0, UsesDaylightSaving = false },
            new Zone { Name = "GMT+12 Kamchatka", Bias = -720, StandardName = 0x50455454, DaylightName = 0x00000000, StandardDate = 0x00000000, DaylightDate = 0x00000000, StandardBias = 0, DaylightBias = 0, UsesDaylightSaving = false },
            new Zone { Name = "GMT+12 Magadan", Bias = -720, StandardName = 0x4D475354, DaylightName = 0x00000000, StandardDate = 0x00000000, DaylightDate = 0x00000000, StandardBias = 0, DaylightBias = 0, UsesDaylightSaving = false },
            new Zone { Name = "GMT+12 Auckland", Bias = -720, StandardName = 0x4E5A5354, DaylightName = 0x4E5A4454, StandardDate = 0x04010003, DaylightDate = 0x09050002, StandardBias = 0, DaylightBias = -60, UsesDaylightSaving = true },
            new Zone { Name = "GMT+12 Fiji Islands", Bias = -720, StandardName = 0x46535400, DaylightName = 0x00000000, StandardDate = 0x00000000, DaylightDate = 0x00000000, StandardBias = 0, DaylightBias = 0, UsesDaylightSaving = false },
            new Zone { Name = "GMT+13 Nuku'alofa", Bias = -780, StandardName = 0x54535400, DaylightName = 0x00000000, StandardDate = 0x00000000, DaylightDate = 0x00000000, StandardBias = 0, DaylightBias = 0, UsesDaylightSaving = false },
            new Zone { Name = "GMT+14 Kiribati", Bias = -840, StandardName = 0x4B535400, DaylightName = 0x00000000, StandardDate = 0x00000000, DaylightDate = 0x00000000, StandardBias = 0, DaylightBias = 0, UsesDaylightSaving = false },
        };

        /// <summary>Each locale's name and the country byte XamSetCountry takes.</summary>
        public static readonly KeyValuePairs Countries = new KeyValuePairs
        {
            { "Argentina", 4 },
            { "Australia", 6 },
            { "Austria", 5 },
            { "Belgium", 8 },
            { "Brazil", 13 },
            { "Canada", 16 },
            { "Chile", 19 },
            { "Colombia", 21 },
            { "Czech Republic", 23 },
            { "Denmark", 25 },
            { "Finland", 32 },
            { "France", 34 },
            { "Germany", 24 },
            { "Greece", 37 },
            { "Hong Kong SAR", 39 },
            { "Hungary", 42 },
            { "India", 46 },
            { "Ireland", 44 },
            { "Israel", 45 },
            { "Italy", 50 },
            { "Japan", 53 },
            { "Korea", 56 },
            { "Mexico", 71 },
            { "Netherlands", 74 },
            { "New Zealand", 76 },
            { "Norway", 75 },
            { "Poland", 82 },
            { "Portugal", 84 },
            { "Russia", 88 },
            { "Saudi Arabia", 89 },
            { "Singapore", 91 },
            { "Slovakia", 93 },
            { "South Africa", 109 },
            { "Spain", 31 },
            { "Sweden", 90 },
            { "Switzerland", 18 },
            { "Taiwan", 101 },
            { "Turkey", 99 },
            { "United Arab Emirates", 1 },
            { "United Kingdom", 35 },
            { "United States", 103 },
            { "Other", 111 },
        };

        /// <summary>The language lists, by the set XGetGameRegion picks (0x92285E40).</summary>
        public static readonly Language[][] Languages =
        {
            new[]
            {
                new Language { Name = "English", Value = 1, Keyboard = 0 },
                new Language { Name = "Deutsch", Value = 3, Keyboard = 0 },
                new Language { Name = "Español", Value = 5, Keyboard = 0 },
                new Language { Name = "Français", Value = 4, Keyboard = 0 },
                new Language { Name = "Italiano", Value = 6, Keyboard = 0 },
                new Language { Name = "Nederlands", Value = 16, Keyboard = 0 },
                new Language { Name = "Norsk (Bokmål)", Value = 15, Keyboard = 0 },
                new Language { Name = "Polski", Value = 11, Keyboard = 0 },
                new Language { Name = "Português", Value = 9, Keyboard = 0 },
                new Language { Name = "Pусский", Value = 12, Keyboard = 0 },
                new Language { Name = "Svenska", Value = 13, Keyboard = 0 },
                new Language { Name = "Türkçe", Value = 14, Keyboard = 0 },
                new Language { Name = "日本語 (スマート フォン式入力)", Value = 2, Keyboard = 5 },
                new Language { Name = "日本語 (50 音入力)", Value = 2, Keyboard = 6 },
                new Language { Name = "한국어", Value = 7, Keyboard = 0 },
                new Language { Name = "中文(繁體-英文)", Value = 8, Keyboard = 2 },
                new Language { Name = "中文(繁體-注音)", Value = 8, Keyboard = 3 },
                new Language { Name = "中文(繁體-倉頡)", Value = 8, Keyboard = 4 },
                new Language { Name = "中文(简体)", Value = 17, Keyboard = 0 },
            },
            new[]
            {
                new Language { Name = "日本語 (スマート フォン式入力)", Value = 2, Keyboard = 5 },
                new Language { Name = "日本語 (50 音入力)", Value = 2, Keyboard = 6 },
                new Language { Name = "한국어", Value = 7, Keyboard = 0 },
                new Language { Name = "中文(繁體-英文)", Value = 8, Keyboard = 2 },
                new Language { Name = "中文(繁體-注音)", Value = 8, Keyboard = 3 },
                new Language { Name = "中文(繁體-倉頡)", Value = 8, Keyboard = 4 },
                new Language { Name = "中文(简体)", Value = 17, Keyboard = 0 },
                new Language { Name = "English", Value = 1, Keyboard = 0 },
                new Language { Name = "Deutsch", Value = 3, Keyboard = 0 },
                new Language { Name = "Español", Value = 5, Keyboard = 0 },
                new Language { Name = "Français", Value = 4, Keyboard = 0 },
                new Language { Name = "Italiano", Value = 6, Keyboard = 0 },
                new Language { Name = "Nederlands", Value = 16, Keyboard = 0 },
                new Language { Name = "Norsk (Bokmål)", Value = 15, Keyboard = 0 },
                new Language { Name = "Polski", Value = 11, Keyboard = 0 },
                new Language { Name = "Português", Value = 9, Keyboard = 0 },
                new Language { Name = "Pусский", Value = 12, Keyboard = 0 },
                new Language { Name = "Svenska", Value = 13, Keyboard = 0 },
                new Language { Name = "Türkçe", Value = 14, Keyboard = 0 },
            },
            new[]
            {
                new Language { Name = "한국어", Value = 7, Keyboard = 0 },
                new Language { Name = "日本語 (スマート フォン式入力)", Value = 2, Keyboard = 5 },
                new Language { Name = "日本語 (50 音入力)", Value = 2, Keyboard = 6 },
                new Language { Name = "中文(繁體-英文)", Value = 8, Keyboard = 2 },
                new Language { Name = "中文(繁體-注音)", Value = 8, Keyboard = 3 },
                new Language { Name = "中文(繁體-倉頡)", Value = 8, Keyboard = 4 },
                new Language { Name = "中文(简体)", Value = 17, Keyboard = 0 },
                new Language { Name = "English", Value = 1, Keyboard = 0 },
                new Language { Name = "Deutsch", Value = 3, Keyboard = 0 },
                new Language { Name = "Español", Value = 5, Keyboard = 0 },
                new Language { Name = "Français", Value = 4, Keyboard = 0 },
                new Language { Name = "Italiano", Value = 6, Keyboard = 0 },
                new Language { Name = "Nederlands", Value = 16, Keyboard = 0 },
                new Language { Name = "Norsk (Bokmål)", Value = 15, Keyboard = 0 },
                new Language { Name = "Polski", Value = 11, Keyboard = 0 },
                new Language { Name = "Português", Value = 9, Keyboard = 0 },
                new Language { Name = "Pусский", Value = 12, Keyboard = 0 },
                new Language { Name = "Svenska", Value = 13, Keyboard = 0 },
                new Language { Name = "Türkçe", Value = 14, Keyboard = 0 },
            },
            new[]
            {
                new Language { Name = "中文(简体)", Value = 17, Keyboard = 0 },
                new Language { Name = "中文(繁體-英文)", Value = 8, Keyboard = 2 },
                new Language { Name = "中文(繁體-注音)", Value = 8, Keyboard = 3 },
                new Language { Name = "中文(繁體-倉頡)", Value = 8, Keyboard = 4 },
                new Language { Name = "日本語 (スマート フォン式入力)", Value = 2, Keyboard = 5 },
                new Language { Name = "日本語 (50 音入力)", Value = 2, Keyboard = 6 },
                new Language { Name = "한국어", Value = 7, Keyboard = 0 },
                new Language { Name = "English", Value = 1, Keyboard = 0 },
                new Language { Name = "Deutsch", Value = 3, Keyboard = 0 },
                new Language { Name = "Español", Value = 5, Keyboard = 0 },
                new Language { Name = "Français", Value = 4, Keyboard = 0 },
                new Language { Name = "Italiano", Value = 6, Keyboard = 0 },
                new Language { Name = "Nederlands", Value = 16, Keyboard = 0 },
                new Language { Name = "Norsk (Bokmål)", Value = 15, Keyboard = 0 },
                new Language { Name = "Polski", Value = 11, Keyboard = 0 },
                new Language { Name = "Português", Value = 9, Keyboard = 0 },
                new Language { Name = "Pусский", Value = 12, Keyboard = 0 },
                new Language { Name = "Svenska", Value = 13, Keyboard = 0 },
                new Language { Name = "Türkçe", Value = 14, Keyboard = 0 },
            },
        };
    }

    /// <summary>Names with numbers, in order.</summary>
    internal sealed class KeyValuePairs : System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, int>>
    {
        public void Add(string key, int value)
        {
            Add(new System.Collections.Generic.KeyValuePair<string, int>(key, value));
        }
    }
}
