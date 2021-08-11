namespace Realtorist.GeoCoding.Implementations.Here.Models
{
    internal class HereAutoCompleteResponse
    {
        public HereAutoCompleteResponseItem[] Items { get; set; }

        public class HereAutoCompleteResponseItem
        {
            public HereAutoCompleteResponseAddress Address { get; set; }

            public class HereAutoCompleteResponseAddress
            {
                public string Label { get; set; }
            }
        }
    }
}
