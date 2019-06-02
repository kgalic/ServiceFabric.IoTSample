using System;
using System.Collections.Generic;
using System.Fabric;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IoTSample.Communication;
using Microsoft.Azure.ServiceBus;
using Microsoft.ServiceFabric.Data.Collections;
using Microsoft.ServiceFabric.Services.Communication.Runtime;
using Microsoft.ServiceFabric.Services.Runtime;

namespace IoTSample.ProcessingStatefulService
{
    /// <summary>
    /// An instance of this class is created for each service replica by the Service Fabric runtime.
    /// </summary>
    internal sealed class ProcessingStatefulService : StatefulService, IServiceBusMessageReceiver
    {
        public ProcessingStatefulService(StatefulServiceContext context)
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
            var connectionString = "service bus connection string from Shared Access Policies <- Azure portal";
            var queueName = "name of the queue";
            yield return new ServiceReplicaListener(context => new ServiceBusCommunicationListener(
                this,
                connectionString,
                queueName), "StatefulService.QueueListener");
        }

        /// <summary>
        /// Override to handle exceptions occured by message receiver.
        /// </summary>
        /// <returns>Task</returns>
        public Task HandleMessageReceivedFailedException(Exception e)
        {
            ServiceEventSource.Current.ServiceMessage(
                                          this.Context,
                                          "Exception occured in Stateful Processing Service: {0}",
                                          e.Message);
            return Task.FromResult(true);
        }

        /// <summary>
        /// Override to handle messages from service bus queue.
        /// </summary>
        /// <returns>Task</returns>
        public Task ReceiveMessageAsync(Message message)
        {
            var messageString = System.Text.Encoding.Default.GetString(message.Body.ToArray());
            ServiceEventSource.Current.ServiceMessage(
                                          this.Context,
                                          "Received Message in Stateful Processing Service: {0}",
                                          messageString);
            return Task.FromResult(true);
        }
    }
}
