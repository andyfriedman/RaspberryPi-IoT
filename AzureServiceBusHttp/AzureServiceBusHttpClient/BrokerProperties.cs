using System;
using System.Globalization;
using System.Runtime.Serialization;

public enum MessageState
{
    Active,
    Deferred,
    Scheduled,
}

namespace AzureServiceBusHttpClient
{
    [DataContract]
    public class BrokerProperties
    {
        [DataMember(EmitDefaultValue = false)]
        public string CorrelationId;

        [DataMember(EmitDefaultValue = false)]
        public string SessionId;

        [DataMember(EmitDefaultValue = false)]
        public int? DeliveryCount;

        [DataMember(EmitDefaultValue = false)]
        public Guid? LockToken;

        [DataMember(EmitDefaultValue = false)]
        public string MessageId;

        [DataMember(EmitDefaultValue = false)]
        public string Label;

        [DataMember(EmitDefaultValue = false)]
        public string ReplyTo;

        [DataMember(EmitDefaultValue = false)]
        public long? SequenceNumber;

        [DataMember(EmitDefaultValue = false)]
        public string To;

        public DateTime? LockedUntilUtcDateTime;

        [DataMember(EmitDefaultValue = false)]
        public string LockedUntilUtc
        {
            get
            {
                if (LockedUntilUtcDateTime != null && LockedUntilUtcDateTime.HasValue)
                {
                    return LockedUntilUtcDateTime.Value.ToString("R", CultureInfo.InvariantCulture);
                }

                return null;
            }
            set
            {
                try
                {
                    LockedUntilUtcDateTime = DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
                }
                catch
                {
                }
            }
        }

        public DateTime? ScheduledEnqueueTimeUtcDateTime;

        [DataMember(EmitDefaultValue = false)]
        public string ScheduledEnqueueTimeUtc
        {
            get
            {
                if (ScheduledEnqueueTimeUtcDateTime != null && ScheduledEnqueueTimeUtcDateTime.HasValue)
                {
                    return ScheduledEnqueueTimeUtcDateTime.Value.ToString("R", CultureInfo.InvariantCulture);
                }

                return null;
            }
            set
            {
                try
                {
                    ScheduledEnqueueTimeUtcDateTime = DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
                }
                catch
                {
                }
            }
        }

        public TimeSpan? TimeToLiveTimeSpan;

        [DataMember(EmitDefaultValue = false)]
        public double TimeToLive
        {
            get
            {
                if (TimeToLiveTimeSpan != null && TimeToLiveTimeSpan.HasValue)
                {
                    return TimeToLiveTimeSpan.Value.TotalSeconds;
                }
                return 0;
            }
            set
            {
                // This is needed as TimeSpan.FromSeconds(TimeSpan.MaxValue.TotalSeconds) throws Overflow exception.
                if (TimeSpan.MaxValue.TotalSeconds == value)
                {
                    TimeToLiveTimeSpan = TimeSpan.MaxValue;
                }
                else
                {
                    TimeToLiveTimeSpan = TimeSpan.FromSeconds(value);
                }
            }
        }

        [DataMember(EmitDefaultValue = false)]
        public string ReplyToSessionId;

        public MessageState StateEnum;

        [DataMember(EmitDefaultValue = false)]
        public string State
        {
            get { return StateEnum.ToString(); }

            internal set { StateEnum = (MessageState)Enum.Parse(typeof(MessageState), value); }
        }

        [DataMember(EmitDefaultValue = false)]
        public long? EnqueuedSequenceNumber;

        [DataMember(EmitDefaultValue = false)]
        public string PartitionKey;

        public DateTime? EnqueuedTimeUtcDateTime;

        [DataMember(EmitDefaultValue = false)]
        public string EnqueuedTimeUtc
        {
            get
            {
                if (EnqueuedTimeUtcDateTime != null && EnqueuedTimeUtcDateTime.HasValue)
                {
                    return EnqueuedTimeUtcDateTime.Value.ToString("R", CultureInfo.InvariantCulture);
                }

                return null;
            }
            set
            {
                try
                {
                    EnqueuedTimeUtcDateTime = DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
                }
                catch
                {
                }
            }
        }

        [DataMember(EmitDefaultValue = false)]
        public string ViaPartitionKey;

        [DataMember(EmitDefaultValue = false)]
        public bool? ForcePersistence;
    }
}