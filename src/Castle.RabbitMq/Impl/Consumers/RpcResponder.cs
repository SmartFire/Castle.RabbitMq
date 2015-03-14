namespace Castle.RabbitMq
{
    using System;
    using RabbitMQ.Client;

    class RpcResponder<T, TResponse> : IMessageConsumer<T>
    {
        private readonly IModel _model;
        private readonly IRabbitSerializer _serializer;
        private readonly Func<MessageEnvelope<T>, IMessageAck, TResponse> _onRespond;

        public RpcResponder(IModel model, 
            IRabbitSerializer serializer, 
            Func<MessageEnvelope<T>, IMessageAck, TResponse> onRespond)
        {
            _model = model;
            _serializer = serializer;
            _onRespond = onRespond;
        }

        public void OnNext(MessageEnvelope<T> newMsg)
        {
            var response = _onRespond(newMsg, null);

            var prop = newMsg.Properties;
            var replyQueue = prop.ReplyTo;
            var correlationId = prop.CorrelationId;

            var newProp = _model.CreateBasicProperties();
            newProp.CorrelationId = correlationId;

            byte[] replyData = null;

            if (typeof(TResponse) == typeof(byte[]))
            {
                // ugly, but should be safe
                replyData = (byte[]) (object) response;
            }
            else
            {
                replyData = _serializer.Serialize(response);
            }

            lock (_model)
            {
                _model.BasicPublish("", replyQueue, newProp, replyData);
            }
        }
    }
}