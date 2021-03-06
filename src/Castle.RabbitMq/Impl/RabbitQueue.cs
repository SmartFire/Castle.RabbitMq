﻿namespace Castle.RabbitMq
{
	using System;
	using RabbitMQ.Client;
	using RabbitMQ.Client.Framing;

	[System.Diagnostics.DebuggerDisplay("Queue '{Name}' {_queueOptions}", Name = "Queue")]
	public class RabbitQueue : IRabbitQueue
	{
		private readonly IModel _model;
		private readonly IRabbitSerializer _defaultSerializer;
		private readonly QueueOptions _queueOptions;

		public RabbitQueue(IModel model, 
						   IRabbitSerializer serializer,
						   QueueDeclareOk result, 
						   QueueOptions queueOptions)
		{
			_model = model;
			_defaultSerializer = serializer ?? queueOptions.Serializer;
			_queueOptions = queueOptions;

			this.Name = result.QueueName;
			this.ConsumerCount = result.ConsumerCount;
			this.MessageCount = result.MessageCount;
		}

		public string Name { get; private set; }
		public uint ConsumerCount { get; private set; }
		public uint MessageCount { get; private set; }

		public IRabbitSerializer DefaultSerializer
		{
			get { return _defaultSerializer; }
		}

		public void Purge()
		{
			lock(_model)
				_model.QueuePurge(this.Name);
		}

		public void Delete()
		{
			lock(_model)
				_model.QueueDelete(this.Name);
		}

		public void Delete(bool ifUnused, bool ifEmpty)
		{
			lock(_model)
				_model.QueueDelete(this.Name, ifUnused, ifEmpty);
		}

		#region IRabbitQueueConsumer

		public Subscription RespondRaw(Func<MessageEnvelope, IMessageAck, MessageEnvelope> onRespond, 
									   ConsumerOptions options)
		{
			Argument.NotNull(onRespond, "onRespond");

			options = options ?? new ConsumerOptions();

			return InternalRespondRaw(onRespond, options, options.ShouldSerializeExceptions);
		}

		public Subscription ConsumeRaw(Action<MessageEnvelope, IMessageAck> onReceived, 
									   ConsumerOptions options)
		{
			Argument.NotNull(onReceived, "onReceived");
			options = options ?? ConsumerOptions.Default;

			var consumer = CreateConsumer(options);

			consumer.Subscribe(new ActionAdapter(env =>
			{
				var msgAcker = new MessageAck(() => { lock(_model) _model.BasicAck(env.DeliveryTag, false); },
					(requeue) => { lock(_model) _model.BasicNack(env.DeliveryTag, false, requeue); });

				onReceived(env, msgAcker);
			}));

			lock(_model)
			{
				var consumerTag = _model.BasicConsume(this.Name, options.NoAck, consumer);

				return new Subscription(_model, consumerTag);
			}
		}

		
		#endregion

		private Subscription InternalRespondRaw(Func<MessageEnvelope, IMessageAck, MessageEnvelope> onRespond, 
			ConsumerOptions options, 
			bool shouldSerializeExceptions)
		{
			options = options ?? ConsumerOptions.Default;
			var serializer = options.Serializer ?? _defaultSerializer;

			var consumer = CreateConsumer(options);

			consumer.Subscribe(new RpcResponder(_model, serializer, onRespond, shouldSerializeExceptions));

			lock (_model)
			{
				var consumerTag = _model.BasicConsume(this.Name, options.NoAck, consumer);

				return new Subscription(_model, consumerTag);
			}
		}

		private IRabbitMessageProducer CreateConsumer(ConsumerOptions options)
		{
			if (options.ConsumerStrategy == ConsumerStrategy.Default)
			{
				return new StreamerConsumer(_model);
			}
			// else if (options.ConsumerStrategy == ConsumerStrategy.Queue)
			{
				return new SharedQueueConsumer(_model);
			}
		}
	}
}