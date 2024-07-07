using System.Threading;

namespace AeonHacs.Utilities
{
    public class Notice
    {
        public static Sender DefaultSender = new Sender();

        /// <summary>
        /// Sends a Notice to all of the default sender's OnNotice event subscribers.
        /// </summary>
        public static Notice Send(string text, Notice.Type type = Notice.Type.Request)
        {
            return Send("", text, type);
        }

        /// <summary>
        /// Sends a Notice to all of the default sender's OnNotice event subscribers.
        /// </summary>
        public static Notice Send(string caption, string text, Notice.Type type = Notice.Type.Request)
        {
            if (DefaultSender.OnNotice != null)
                return DefaultSender.Send(caption, text, type);
            else
                return null;
        }
        /// <summary>
        /// Sends an OkCancel Notice and checks whether the response is "Ok"
        /// </summary>
        /// <returns>true if the operator responds Ok; otherwise, false. </returns>
        public static bool Ok(string caption, string text) =>
            Send(caption, text, Type.OkCancel) is Notice reply && reply.Text == "Ok";



        // If the type is Tell, the notice is processed in a newly
        // spawned background thread, and control returns to the caller
        // immediately without waiting for it to complete.
        public enum Type
        {
            /// <summary>
            /// The recipient is expected to respond with a Notice or a null value. The sender
            /// waits until a response is received.
            /// </summary>
            Request,
            /// <summary>
            /// The sender waits for the recipient to respond with a Notice,
            /// which generally is expected to contain the Text "Ok" or "Cancel".
            /// However, the response may be any valid Notice or a null value.
            /// </summary>
            OkCancel,
            /// <summary>
            /// The recipient is expected to respond with a Notice or a null value. The sender
            /// waits until the response is received. The recipient may (optionally) handle the Notice
            /// as a Warning, and may return a Notice containing the text "Ok" or "Cancel".
            /// </summary>
            Warn,
            /// <summary>
            /// The Notice is transmitted to the recipient, but the response is ignored.
            /// </summary>
            Tell
        }

        /// <summary>
        /// A title or subject for the Notice.
        /// </summary>
        public string Caption;
        /// <summary>
        /// The message or body of the Notice.
        /// </summary>
        public string Text;
        /// <summary>
        /// A generalized tag intended to convey the context of the Notice. If the Type is Tell,
        /// any response is ignored, and the Sender does not wait for one. With any other Type,
        /// the Sender waits for a response Notice (or null value) from the recipient.
        /// </summary>
        public Type NoticeType;

        public Notice(string text, string caption = null, Type type = Type.Request)
        {
            Caption = caption;
            Text = text;
            NoticeType = type;
        }

        public class Sender
        {
            public delegate Notice Handler(Notice notice);
            public Handler OnNotice;

            /// <summary>
            /// Sends a Notice to all of this sender's OnNotice event subscribers.
            /// </summary>
            public Notice Send(string text, Notice.Type type = Notice.Type.Request)
            {
                return Send("", text, type);
            }

            /// <summary>
            /// Sends a Notice to all of this sender's OnNotice event subscribers.
            /// </summary>
            /// <param name="caption"></param>
            /// <param name="text"></param>
            /// <param name="type"></param>
            /// <returns></returns>
            public Notice Send(string caption, string text, Notice.Type type = Notice.Type.Request)
            {
                var notice = new Notice(text, caption, type);
                if (type == Type.Tell)
                {
                    new Thread(new ThreadStart(delegate
                    { OnNotice?.Invoke(notice); }))
                    { IsBackground = true }.Start();

                    return null;
                }
                else
                {
                    return OnNotice?.Invoke(notice);
                }
            }
        }
    }
}
