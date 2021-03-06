﻿using System;

namespace Atata
{
    public class WaitForDocumentReadyStateAttribute : TriggerAttribute
    {
        public WaitForDocumentReadyStateAttribute(TriggerEvents on = TriggerEvents.Init, TriggerPriority priority = TriggerPriority.Medium)
            : base(on, priority)
        {
        }

        protected internal override void Execute<TOwner>(TriggerContext<TOwner> context)
        {
            bool completed = context.Driver.Try().Until(
                x => (bool)context.Driver.ExecuteScript("return document.readyState === 'complete'"));

            if (!completed)
                throw new TimeoutException("Timed out waiting for document to be loaded/ready.");
        }
    }
}
