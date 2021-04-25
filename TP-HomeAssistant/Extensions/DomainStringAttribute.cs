using System;

namespace TP_HomeAssistant.Extensions
{
    public class DomainStringAttribute : Attribute
    {
        public string DomainString { get; }
        public DomainStringAttribute(string domainString)
        {
            this.DomainString = domainString;
        }
    }
}
