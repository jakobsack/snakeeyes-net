﻿using System;
using System.Collections.Specialized;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Xml;
using System.Xml.Serialization;

namespace SnakeEyes
{
    [XmlRoot("TraceEvent")]
    public class FileTraceEvent
    {
        public string Source { get; set; }
        public string MachineName { get; set; }
        public string Timestamp { get; set; }
        public int EventId { get; set; }
        public TraceEventType EventType { get; set; }
        public string FileName { get; set; }
        public long Value { get; set; }
        public long MaxValue { get; set; }
        public string Message { get; set; }
    };


    public class FileProbe : IProbe
    {
        TraceSource _traceSource;

        public string FileName { get; set; }
        public TimeSpan ProbeFrequency { get; set; }
        public long MaxFileSize { get; set; }
        public TimeSpan MaxFileAge { get; set; }
        public int EventId { get; set; }
        public TraceEventType EventType { get; set; }
        public long? DefaultFileSize { get; set; }
        public int? DefaultFileAge { get; set; }

        public FileProbe()
        {
            ProbeFrequency = TimeSpan.FromSeconds(5);
            EventType = TraceEventType.Warning;
        }

        public void Dispose()
        {
            // NOOP
        }

        public TraceSource ConfigureProbe(string configName)
        {
            _traceSource = new TraceSource(configName);
            NameValueCollection config = (NameValueCollection)ConfigurationManager.GetSection(configName);
            if (config != null)
            {
                FileName = config["FileName"];
                if (config["MaxFileSize"] != null)
                    MaxFileSize = long.Parse(config["MaxFileSize"]);
                if (config["MaxFileAge"] != null)
                    MaxFileAge = TimeSpan.FromSeconds(Int32.Parse(config["MaxFileAge"]));
                if (config["DefaultFileSize"] != null)
                    DefaultFileSize = long.Parse(config["DefaultFileSize"]);
                if (config["DefaultFileAge"] != null)
                    DefaultFileAge = int.Parse(config["DefaultFileAge"]);
                if (config["ProbeFrequency"] != null)
                    ProbeFrequency = TimeSpan.FromSeconds(Int32.Parse(config["ProbeFrequency"]));
                if (config["EventId"] != null)
                    EventId = Int32.Parse(config["EventId"]);
                if (config["EventType"] != null)
                    EventType = (TraceEventType)Enum.Parse(typeof(TraceEventType), config["EventType"]);
            }

            return _traceSource;
        }

        public TimeSpan ExecuteProbe()
        {
            try
            {
                if (!File.Exists(FileName))
                {
                    if (DefaultFileSize.HasValue || DefaultFileAge.HasValue)
                    {
                        TraceEvent(TraceEventType.Information, DefaultFileSize ?? 0, DefaultFileAge ?? 0, "File not found");
                        return ProbeFrequency;
                    }
                    else
                    {
                        throw new FileNotFoundException(_traceSource.Name + " missing file", FileName);
                    }
                }

                var fileInfo = new FileInfo(FileName);
                long fileSize = fileInfo.Length;
                int fileAge = (int)Math.Max(0.0, (DateTime.UtcNow - fileInfo.LastWriteTimeUtc).TotalSeconds);
                TraceEvent(TraceEventType.Information, fileSize, fileAge, "Ok");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine(_traceSource.Name + " " + ex.Message);
                TraceEvent(TraceEventType.Critical, 0, 0, ex.Message); // Report initial exception
            }
            return ProbeFrequency;
        }

        void TraceEvent(TraceEventType eventType, long fileSize, int fileAge, string message)
        {
            long value = fileSize;
            long maxValue = MaxFileSize;
            if (eventType == TraceEventType.Information)
            {
                if (fileSize > MaxFileSize)
                {
                    message = "FileSize is above maximum threshold";
                    eventType = EventType;
                }
                else if (fileAge > MaxFileAge.TotalSeconds && MaxFileAge.TotalSeconds > 0)
                {
                    message = "FileAge is above maximum threshold";
                    eventType = EventType;
                    value = fileAge;
                    maxValue = (int)MaxFileAge.TotalSeconds;
                }
            }

            FileTraceEvent traceEvent = new FileTraceEvent();
            traceEvent.Source = _traceSource.Name;
            traceEvent.MachineName = Environment.MachineName;
            traceEvent.Timestamp = DateTime.Now.ToString("yyyy'-'MM'-'dd' 'HH':'mm':'ss");
            traceEvent.Value = value;
            traceEvent.MaxValue = maxValue;
            traceEvent.EventId = EventId;
            traceEvent.EventType = eventType;
            traceEvent.Message = message;

            string formatMessage = "";

            using (StringWriter writer = new StringWriter())
            using (XmlTextWriter xmlwriter = new XmlTextWriter(writer))
            {
                xmlwriter.Formatting = Formatting.Indented;
                XmlSerializerNamespaces xmlnsEmpty = new XmlSerializerNamespaces();
                xmlnsEmpty.Add("", "");
                XmlSerializer serializer = new XmlSerializer(typeof(FileTraceEvent));
                serializer.Serialize(xmlwriter, traceEvent, xmlnsEmpty);
                formatMessage = writer.ToString();
            }

            try
            {
                _traceSource.TraceEvent(eventType, EventId, formatMessage);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError(_traceSource.Name + " " + ex.Message);
                if (ex.InnerException != null)
                    System.Diagnostics.Trace.TraceError(_traceSource.Name + " " + ex.InnerException.Message);
                System.Diagnostics.Trace.WriteLine(_traceSource.Name + " failed to trace event. Check TraceListeners:");
                foreach (TraceListener listener in _traceSource.Listeners)
                    System.Diagnostics.Trace.WriteLine(_traceSource.Name + " has listener: " + listener.Name + " (" + listener.ToString() + ")");
                System.Diagnostics.Trace.WriteLine(_traceSource.Name + " " + ex.ToString());
            }
        }
    }
}

