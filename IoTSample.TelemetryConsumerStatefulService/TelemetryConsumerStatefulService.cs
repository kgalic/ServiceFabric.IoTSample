using System;
using System.Collections.Generic;
using System.Fabric;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IoTSample.Communication;
using Microsoft.Azure.EventHubs;
using Microsoft.Azure.ServiceBus;
using Microsoft.ServiceFabric.Data;
using Microsoft.ServiceFabric.Data.Collections;
using Microsoft.ServiceFabric.Services.Client;
using Microsoft.ServiceFabric.Services.Communication.Client;
using Microsoft.ServiceFabric.Services.Communication.Runtime;
using Microsoft.ServiceFabric.Services.Runtime;

namespace IoTSample.TelemetryConsumerStatefulService
{
    /// <summary>
    /// An instance of this class is created for each service replica by the Service Fabric runtime.
    /// </summary>
    internal sealed class TelemetryConsumerStatefulService : StatefulService
    {
        /// <summary>
        /// The offset interval specifies how frequently the offset is saved.
        /// A lower value will save more often which can reduce repeat message processing at the cost of performance. 
        /// </summary>
        private const int OffsetInterval = 5;

        /// <summary>
        /// The maximum number of messages that partition receiver will read. 
        /// </summary>
        private const int MaxMessageCount = 1;

        /// <summary>
        /// Names of the dictionaries that hold the current offset value and partition epoch.
        /// </summary>
        private const string OffsetDictionaryName = "OffsetDictionary";
        private const string EpochDictionaryName = "EpochDictionary";

        public TelemetryConsumerStatefulService(StatefulServiceContext context)
            : base(context)
        { }

        /// <summary>
        /// Optional override to create listeners (e.g., HTTP, Service Remoting, WCF, etc.) for this service replica to handle client or user requests.
        /// </summary>
        /// <remarks>
        /// For more information on service communication, see https://aka.ms/servicefabricservicecommunication
        /// </remarks>
        /// <returns>A collection of listeners.</returns>
        protected override IEnumerable<ServiceReplicaListener> CreateServiceReplicaListeners()
        {
            return new ServiceReplicaListener[0];
        }

        /// <summary>
        /// This is the main entry point for your service replica.
        /// This method executes when this replica of your service becomes primary and has write status.
        /// </summary>
        /// <param name="cancellationToken">Canceled when Service Fabric needs to shut down this service replica.</param>
        protected override async Task RunAsync(CancellationToken cancellationToken)
        {
            string eventHubConnectionString = "Event hub compatible endpoint - Azure portal -> IoT Hub -> Build-in endpoints ";

            // These Reliable Dictionaries are used to keep track of our position in IoT Hub.
            // If this service fails over, this will allow it to pick up where it left off in the event stream.
            IReliableDictionary<string, string> offsetDictionary =
                await this.StateManager.GetOrAddAsync<IReliableDictionary<string, string>>(OffsetDictionaryName);

            IReliableDictionary<string, long> epochDictionary =
                await this.StateManager.GetOrAddAsync<IReliableDictionary<string, long>>(EpochDictionaryName);

            // Each partition of this service corresponds to a partition in IoT Hub.
            // IoT Hub partitions are numbered 0..n-1, up to n = 32.
            // This service needs to use an identical partitioning scheme. 
            // The low key of every partition corresponds to an IoT Hub partition.
            Int64RangePartitionInformation partitionInfo = (Int64RangePartitionInformation)this.Partition.PartitionInfo;
            long servicePartitionKey = partitionInfo.LowKey;
            
            PartitionReceiver partitionReceiver = null;

            try
            {
                // Get the partition receiver for reading message from specific consumer group and partition
                // Consumer group is set to $Default
                partitionReceiver = await this.ConnectToIoTHubAsync(consumerGroup: PartitionReceiver.DefaultConsumerGroupName,
                                                                    connectionString: eventHubConnectionString, 
                                                                    servicePartitionKey: servicePartitionKey, 
                                                                    epochDictionary: epochDictionary, 
                                                                    offsetDictionary: offsetDictionary);

                int offsetIteration = 0;

                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        // Reading event data list from partition receiver with max message count parameter
                        IEnumerable<EventData> eventDataList = await partitionReceiver.ReceiveAsync(MaxMessageCount);
                        
                        if (eventDataList == null)
                        {
                            continue;
                        }

                        using (var eventData = eventDataList.FirstOrDefault())
                        {
                            string deviceId = (string)eventData.SystemProperties["iothub-connection-device-id"];
                            string data = Encoding.UTF8.GetString(eventData.Body);
                            ServiceEventSource.Current.ServiceMessage(
                                        this.Context,
                                        "Reading data from device {0} with data {1}",
                                        deviceId,
                                        data);

                            var message = new Message(eventData.Body.Array);


                            if (++offsetIteration % OffsetInterval == 0)
                            {
                                ServiceEventSource.Current.ServiceMessage(
                                        this.Context,
                                        "Saving offset {0}",
                                        eventData.SystemProperties.Offset);

                                using (ITransaction tx = this.StateManager.CreateTransaction())
                                {
                                    await offsetDictionary.SetAsync(tx, "offset", eventData.SystemProperties.Offset);
                                    await tx.CommitAsync();
                                }

                                offsetIteration = 0;
                            }
                        }
                    }
                    catch (TimeoutException te)
                    {
                        // transient error. Retry.
                        ServiceEventSource.Current.ServiceMessage(this.Context, $"TimeoutException in RunAsync: {te.ToString()}");
                    }
                    catch (FabricTransientException fte)
                    {
                        // transient error. Retry.
                        ServiceEventSource.Current.ServiceMessage(this.Context, $"FabricTransientException in RunAsync: {fte.ToString()}");
                    }
                    catch (FabricNotPrimaryException)
                    {
                        // not primary any more, time to quit.
                        return;
                    }
                    catch (Exception ex)
                    {
                        ServiceEventSource.Current.ServiceMessage(this.Context, ex.ToString());

                        throw;
                    }
                }
            }
            finally
            {
                if (partitionReceiver != null)
                {
                    await partitionReceiver.CloseAsync();
                }
            }
        }

        /// <summary>
        /// Creates an EventHubReceiver from the given connection sting and partition key.
        /// The Reliable Dictionaries are used to create a receiver from wherever the service last left off,
        /// or from the current date/time if it's the first time the service is coming up.
        /// </summary>
        /// <param name="connectionString"></param>
        /// <param name="servicePartitionKey"></param>
        /// <param name="epochDictionary"></param>
        /// <param name="offsetDictionary"></param>
        /// <returns></returns>
        private async Task<PartitionReceiver> ConnectToIoTHubAsync(
            string consumerGroup,
            string connectionString,
            long servicePartitionKey,
            IReliableDictionary<string, long> epochDictionary,
            IReliableDictionary<string, string> offsetDictionary)
        {
            PartitionReceiver partitionReceiver = null;

            var eventHubClient = EventHubClient.CreateFromConnectionString(connectionString);

            // Get an IoT Hub partition ID that corresponds to this partition's low key.
            // This assumes that this service has a partition count 'n' that is equal to the IoT Hub partition count and a partition range of 0..n-1.
            // For example, given an IoT Hub with 32 partitions, this service should be created with:
            // partition count = 32
            // partition range = 0..31
            string eventHubPartitionId = servicePartitionKey.ToString();

            using (ITransaction tx = this.StateManager.CreateTransaction())
            {
                ConditionalValue<string> offsetResult = await offsetDictionary.TryGetValueAsync(tx, "offset", LockMode.Default);
                ConditionalValue<long> epochResult = await epochDictionary.TryGetValueAsync(tx, "epoch", LockMode.Update);

                long newEpoch = epochResult.HasValue
                    ? epochResult.Value + 1
                    : 0;

                if (offsetResult.HasValue)
                {
                    // continue where the service left off before the last failover or restart.
                    ServiceEventSource.Current.ServiceMessage(
                        this.Context,
                        "Creating EventHub listener on partition {0} with offset {1}",
                        eventHubPartitionId,
                        offsetResult.Value);

                    partitionReceiver = eventHubClient.CreateEpochReceiver(consumerGroupName: consumerGroup,
                                                                           partitionId: eventHubPartitionId,
                                                                           eventPosition: EventPosition.FromOffset(offsetResult.Value),
                                                                           epoch: newEpoch);
                }
                else
                {
                    // first time this service is running so there is no offset value yet.
                    // start with the current time.
                    ServiceEventSource.Current.ServiceMessage(
                        this.Context,
                        "Creating EventHub listener on partition {0} with offset {1}",
                        eventHubPartitionId,
                        DateTime.UtcNow);

                    partitionReceiver = eventHubClient.CreateEpochReceiver(consumerGroupName: consumerGroup,
                                                                           partitionId: eventHubPartitionId,
                                                                           eventPosition: EventPosition.FromEnqueuedTime(DateTime.UtcNow),
                                                                           epoch: newEpoch);
                }

                // epoch is recorded each time the service fails over or restarts.
                await epochDictionary.SetAsync(tx, "epoch", newEpoch);
                await tx.CommitAsync();
            }

            return partitionReceiver;
        }
    }
}
