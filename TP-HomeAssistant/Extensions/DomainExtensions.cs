using System;
using TP_HomeAssistant.Models;
using TP_HomeAssistant.Extensions;
using System.Reflection;

namespace TP_HomeAssistant.Extensions
{
    public static class DomainExtensions
    {
        public static string GetDomainString(this Domain domain)
        {
            return domain.GetAttribute<DomainStringAttribute>()?.DomainString ?? domain.ToString().ToLower();
        }

        private static T GetAttribute<T>(this Domain domain)
            where T : System.Attribute
        {

            MemberInfo[] memberInfo = domain.GetType().GetMember(Enum.GetName(domain.GetType(), domain));
            if(memberInfo.Length > 0)
            {
                T[] customAttributes = memberInfo[0].GetCustomAttributes(typeof(T), inherit: false) as T[];
                if (customAttributes.Length > 0)
                    return customAttributes[0];
            }

            return null;
        }

        public static Domain GetDomainFromString(this string domainString)
        {
            int dot = domainString.IndexOf(".");
            if (dot > 1)
                domainString = domainString.Substring(0, dot);
            foreach (Domain value in Enum.GetValues(typeof(Domain)))
                if (domainString.Equals(value.GetDomainString()))
                    return value;

            throw new UnsupportedDomainException($"{domainString} is not a supported HA domain.");
        }
    }

    public class UnsupportedDomainException : Exception
    {
        public UnsupportedDomainException(string message) : base(message) { }
    }
}
