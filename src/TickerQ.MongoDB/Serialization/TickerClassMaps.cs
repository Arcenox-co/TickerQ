using System.Threading;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;

namespace TickerQ.MongoDB.Serialization
{
    internal static class TickerClassMaps
    {
        private static int _registered;

        public static void RegisterOnce<TTimeTicker, TCronTicker>()
            where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
            where TCronTicker : CronTickerEntity, new()
        {
            if (Interlocked.Exchange(ref _registered, 1) != 0)
                return;

            BsonSerializer.RegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));
            BsonSerializer.RegisterSerializer(typeof(TickerStatus), new EnumSerializer<TickerStatus>(BsonType.Int32));
            BsonSerializer.RegisterSerializer(typeof(RunCondition), new EnumSerializer<RunCondition>(BsonType.Int32));

            RegisterTimeTicker<TTimeTicker>();
            RegisterCronTicker<TCronTicker>();
            RegisterCronOccurrence<TCronTicker>();
        }

        private static void RegisterTimeTicker<TTimeTicker>()
            where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        {
            // Parent/Children navigation props live on the generic intermediate
            // TimeTickerEntity<T> — UnmapMember requires the class map ClassType to
            // be the declaring type, so we register the intermediate first, then the concrete.
            if (!BsonClassMap.IsClassMapRegistered(typeof(TimeTickerEntity<TTimeTicker>)))
            {
                BsonClassMap.RegisterClassMap<TimeTickerEntity<TTimeTicker>>
                (cm =>
                    {
                        cm.AutoMap();
                        cm.SetIgnoreExtraElements(true);
                        cm.UnmapMember(x => x.Parent);
                        cm.UnmapMember(x => x.Children);
                    }
                );
            }

            if (!BsonClassMap.IsClassMapRegistered(typeof(TTimeTicker)))
            {
                BsonClassMap.RegisterClassMap<TTimeTicker>
                (cm =>
                    {
                        cm.AutoMap();
                        cm.SetIgnoreExtraElements(true);
                    }
                );
            }
        }

        private static void RegisterCronTicker<TCronTicker>()
            where TCronTicker : CronTickerEntity, new()
        {
            if (BsonClassMap.IsClassMapRegistered(typeof(TCronTicker)))
                return;

            BsonClassMap.RegisterClassMap<TCronTicker>
            (cm =>
                {
                    cm.AutoMap();
                    cm.SetIgnoreExtraElements(true);
                }
            );
        }

        private static void RegisterCronOccurrence<TCronTicker>()
            where TCronTicker : CronTickerEntity, new()
        {
            if (BsonClassMap.IsClassMapRegistered(typeof(CronTickerOccurrenceEntity<TCronTicker>)))
                return;

            BsonClassMap.RegisterClassMap<CronTickerOccurrenceEntity<TCronTicker>>
            (cm =>
                {
                    cm.AutoMap();
                    cm.SetIgnoreExtraElements(true);
                    cm.UnmapMember(x => x.CronTicker);
                }
            );
        }
    }
}