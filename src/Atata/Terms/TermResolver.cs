﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace Atata
{
    public static class TermResolver
    {
        private const TermCase DefaultFormat = TermCase.Title;
        private const TermMatch DefaultMatch = TermMatch.Equals;

        private static readonly Dictionary<Type, TermConverter> TypeTermConverters;

        static TermResolver()
        {
            TypeTermConverters = new Dictionary<Type, TermConverter>();

            RegisterStandardConverters();
        }

        private static void RegisterStandardConverters()
        {
            RegisterNumberConverter(sbyte.Parse);
            RegisterNumberConverter(byte.Parse);
            RegisterNumberConverter(short.Parse);
            RegisterNumberConverter(ushort.Parse);
            RegisterNumberConverter(int.Parse);
            RegisterNumberConverter(uint.Parse);
            RegisterNumberConverter(long.Parse);
            RegisterNumberConverter(ulong.Parse);
            RegisterNumberConverter(float.Parse);
            RegisterNumberConverter(double.Parse);
            RegisterNumberConverter(decimal.Parse);

            RegisterConverter<DateTime>(
                (s, opt) =>
                {
                    string stringValue = RetrieveValueFromString(s, opt.Format);
                    string concreteFormat = RetrieveConcreteFormatFromStringFormat(opt.Format);

                    if (concreteFormat == null)
                        return DateTime.Parse(stringValue, opt.Culture);
                    else
                        return DateTime.ParseExact(stringValue, concreteFormat, opt.Culture);
                });

            RegisterConverter<TimeSpan>(
                (s, opt) =>
                {
                    string stringValue = RetrieveValueFromString(s, opt.Format);
                    string concreteFormat = RetrieveConcreteFormatFromStringFormat(opt.Format);

                    if (concreteFormat == null)
                        return TimeSpan.Parse(stringValue, opt.Culture);
                    else if (concreteFormat.Contains("t"))
                        return DateTime.ParseExact(stringValue, concreteFormat, opt.Culture).TimeOfDay;
                    else
                        return TimeSpan.ParseExact(stringValue, concreteFormat, opt.Culture);
                },
                (v, opt) =>
                {
                    string concreteFormat = RetrieveConcreteFormatFromStringFormat(opt.Format);

                    if (concreteFormat != null && concreteFormat.Contains("t"))
                    {
                        return FormatValue(
                            DateTime.Today.Add(v).ToString(concreteFormat, opt.Culture),
                            opt.Format,
                            opt.Culture);
                    }
                    else
                    {
                        return FormatValue(v, opt.Format, opt.Culture);
                    }
                });

            RegisterConverter<Guid>(
                (s, opt) =>
                {
                    string stringValue = RetrieveValueFromString(s, opt.Format);
                    string concreteFormat = RetrieveConcreteFormatFromStringFormat(opt.Format);

                    if (concreteFormat == null)
                        return Guid.Parse(stringValue);
                    else
                        return Guid.ParseExact(stringValue, concreteFormat);
                });
        }

        private static void RegisterNumberConverter<T>(
            Func<string, NumberStyles, IFormatProvider, T> parseFunction)
            where T : IFormattable
        {
            RegisterConverter(
                typeof(T),
                (s, opt) =>
                {
                    string stringValue = RetrieveValueFromString(s, opt.Format);
                    string concreteFormat = RetrieveConcreteFormatFromStringFormat(opt.Format);

                    bool isPercentageFormat = concreteFormat != null && concreteFormat.StartsWith("P", StringComparison.InvariantCultureIgnoreCase);

                    if (isPercentageFormat)
                    {
                        stringValue = stringValue.
                            Replace(opt.Culture.NumberFormat.PercentSymbol, string.Empty).
                            Replace(opt.Culture.NumberFormat.PercentDecimalSeparator, opt.Culture.NumberFormat.NumberDecimalSeparator);

                        decimal percent = decimal.Parse(stringValue, NumberStyles.Any, opt.Culture) / 100;
                        return Convert.ChangeType(percent, typeof(T), opt.Culture);
                    }
                    else
                    {
                        return parseFunction(stringValue, NumberStyles.Any, opt.Culture);
                    }
                });
        }

        public static void RegisterConverter<T>(
            Func<string, TermOptions, T> fromStringConverter,
            Func<T, TermOptions, string> toStringConverter = null)
        {
            fromStringConverter.CheckNotNull(nameof(fromStringConverter));

            Func<string, TermOptions, object> castedFromStringConverter = (s, to) => fromStringConverter(s, to);
            Func<object, TermOptions, string> castedToStringConverter = null;
            if (toStringConverter != null)
                castedToStringConverter = (v, to) => toStringConverter((T)v, to);

            RegisterConverter(typeof(T), castedFromStringConverter, castedToStringConverter);
        }

        public static void RegisterConverter(
            Type type,
            Func<string, TermOptions, object> fromStringConverter,
            Func<object, TermOptions, string> toStringConverter = null)
        {
            fromStringConverter.CheckNotNull(nameof(fromStringConverter));

            TypeTermConverters[type] = new TermConverter
            {
                FromStringConverter = fromStringConverter,
                ToStringConverter = toStringConverter
            };
        }

        public static string ToDisplayString(object value, TermOptions termOptions = null)
        {
            if (value is IEnumerable<object>)
                return string.Join("/", ((IEnumerable<object>)value).Select(x => ToDisplayString(x, termOptions)));
            else
                return ToString(value, termOptions);
        }

        public static string ToString(object value, TermOptions termOptions = null)
        {
            if (value == null || Equals(value, string.Empty))
                return null;

            string[] terms = GetTerms(value, termOptions);
            return string.Join("/", terms);
        }

        public static string[] GetTerms(object value, TermOptions termOptions = null)
        {
            value.CheckNotNull(nameof(value));

            termOptions = termOptions ?? new TermOptions();
            TermConverter termConverter;

            if (value is string)
                return new[] { FormatStringValue((string)value, termOptions) };
            else if (value is Enum)
                return GetEnumTerms((Enum)value, termOptions);
            else if (TypeTermConverters.TryGetValue(value.GetType(), out termConverter) && termConverter.ToStringConverter != null)
                return new[] { termConverter.ToStringConverter(value, termOptions) };
            else
                return new[] { FormatValue(value, termOptions.Format, termOptions.Culture) };
        }

        private static string FormatStringValue(string value, TermOptions termOptions)
        {
            if (termOptions.GetCaseOrNull() != null)
                value = termOptions.GetCaseOrNull().Value.ApplyTo(value);

            return FormatValue(value, termOptions.Format, termOptions.Culture);
        }

        private static string FormatValue(object value, string format, CultureInfo culture)
        {
            if (IsComplexStringFormat(format))
                return string.Format(culture, format, value);
            else if (value is IFormattable)
                return ((IFormattable)value).ToString(format, culture);
            else
                return value.ToString();
        }

        private static bool IsComplexStringFormat(string format)
        {
            return format != null && format.Contains("{0");
        }

        private static string RetrieveValueFromString(string value, string format)
        {
            return IsComplexStringFormat(format) ? RetrieveValuePart(value, format) : value;
        }

        private static string RetrieveConcreteFormatFromStringFormat(string format)
        {
            if (string.IsNullOrEmpty(format))
            {
                return null;
            }
            else if (IsComplexStringFormat(format))
            {
                int startIndex = format.IndexOf("{0");
                int endIndex = format.IndexOf('}', startIndex + 2);
                if (endIndex - startIndex == 2)
                    return null;
                else
                    return format.Substring(startIndex + 3, endIndex - startIndex - 3);
            }
            else
            {
                return format;
            }
        }

        private static string RetrieveValuePart(string value, string format)
        {
            if (string.IsNullOrEmpty(format))
                return value;

            string[] formatParts = format.Split(new[] { "{0" }, 2, StringSplitOptions.None);
            formatParts[1] = formatParts[1].Substring(formatParts[1].IndexOf('}') + 1);

            string formatStart = formatParts[0];
            string formatEnd = formatParts[1];

            if (!value.StartsWith(formatStart))
            {
                throw new ArgumentException(
                    "\"{0}\" value doesn't match format \"{1}\". Should start with \"{2}\"".FormatWith(value, format, formatStart),
                    "value");
            }

            if (!value.EndsWith(formatEnd))
            {
                throw new ArgumentException(
                    "\"{0}\" value doesn't match format \"{1}\". Should end with \"{2}\"".FormatWith(value, format, formatEnd),
                    "value");
            }

            return value.Substring(formatStart.Length, value.Length - formatStart.Length - formatEnd.Length);
        }

        public static string CreateXPathCondition(object value, TermOptions termOptions = null, string operand = ".")
        {
            string[] terms = GetTerms(value, termOptions);
            TermMatch match = GetMatch(value, termOptions);
            return match.CreateXPathCondition(terms, operand);
        }

        public static T FromString<T>(string value, TermOptions termOptions = null)
        {
            object result = FromString(value, typeof(T), termOptions);
            return (T)result;
        }

        public static object FromString(string value, Type destinationType, TermOptions termOptions = null)
        {
            object result = string.IsNullOrEmpty(value)
                ? null
                : RetrieveValueFromString(value, destinationType, termOptions ?? new TermOptions());

            if (result == null && !destinationType.IsClassOrNullable())
            {
                throw new ArgumentException(
                    "Failed to find value of type '{0}' corresponding to '{1}'.".FormatWith(destinationType.FullName, value),
                    nameof(value));
            }
            else
            {
                return result;
            }
        }

        private static object RetrieveValueFromString(string value, Type destinationType, TermOptions termOptions)
        {
            Type underlyingType = Nullable.GetUnderlyingType(destinationType) ?? destinationType;
            TermConverter termConverter;

            if (underlyingType.IsEnum)
                return StringToEnum(value, underlyingType, termOptions);
            else if (TypeTermConverters.TryGetValue(underlyingType, out termConverter))
                return termConverter.FromStringConverter(value, termOptions);
            else
                return Convert.ChangeType(RetrieveValuePart(value, termOptions.Format), underlyingType, termOptions.Culture);
        }

        public static object StringToEnum(string value, Type enumType, TermOptions termOptions = null)
        {
            return enumType.GetIndividualEnumFlags().
                FirstOrDefault(x => GetEnumMatch(x, termOptions).IsMatch(value, GetEnumTerms(x, termOptions)));
        }

        public static string[] GetEnumTerms(Enum value, TermOptions termOptions = null)
        {
            return value.GetType().IsDefined(typeof(FlagsAttribute), false)
                ? GetFlagsEnumTerms(value, termOptions)
                : GetIndividualEnumTerms(value, termOptions);
        }

        private static string[] GetFlagsEnumTerms(Enum value, TermOptions termOptions)
        {
            return value.GetIndividualFlags().SelectMany(x => GetIndividualEnumTerms(x, termOptions)).ToArray();
        }

        private static string[] GetIndividualEnumTerms(Enum value, TermOptions termOptions)
        {
            TermAttribute termAttribute = GetEnumTermAttribute(value);
            bool hasTermValue = termAttribute != null && termAttribute.Values != null && termAttribute.Values.Any();

            string termFormat = termOptions.GetFormatOrNull()
                ?? termAttribute.GetFormatOrNull()
                ?? GetTermSettings(value.GetType()).GetFormatOrNull()
                ?? null;

            if (hasTermValue)
            {
                return termAttribute.Values.Select(x => FormatValue(x, termFormat, termOptions.Culture)).ToArray();
            }
            else
            {
                TermCase termCase = termOptions.GetCaseOrNull()
                    ?? termAttribute.GetCaseOrNull()
                    ?? GetTermSettings(value.GetType()).GetCaseOrNull()
                    ?? DefaultFormat;

                if (termFormat == null || termFormat.Contains("{0}"))
                {
                    string term = termCase.ApplyTo(value.ToString());
                    return new[] { FormatValue(term, termFormat, termOptions.Culture) };
                }
                else
                {
                    return new[] { FormatValue(value, termFormat, termOptions.Culture) };
                }
            }
        }

        public static TermMatch GetMatch(object value, ITermSettings termSettings = null)
        {
            if (value is Enum)
                return GetEnumMatch((Enum)value, termSettings);
            else
                return termSettings.GetMatchOrNull() ?? DefaultMatch;
        }

        public static TermMatch GetEnumMatch(Enum value, ITermSettings termSettings = null)
        {
            return termSettings.GetMatchOrNull()
                ?? GetEnumTermAttribute(value).GetMatchOrNull()
                ?? GetTermSettings(value.GetType()).GetMatchOrNull()
                ?? DefaultMatch;
        }

        private static TermAttribute GetEnumTermAttribute(Enum value)
        {
            Type type = value.GetType();
            MemberInfo memberInfo = type.GetMember(value.ToString())[0];

            return memberInfo.GetCustomAttribute<TermAttribute>(false);
        }

        private static ITermSettings GetTermSettings(Type type)
        {
            return type.GetCustomAttribute<TermSettingsAttribute>(false);
        }

        private class TermConverter
        {
            public Func<string, TermOptions, object> FromStringConverter { get; set; }

            public Func<object, TermOptions, string> ToStringConverter { get; set; }
        }
    }
}
