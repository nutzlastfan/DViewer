using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DViewer
{
    public class DicomMetadataItem : INotifyPropertyChanged
    {
        private string _tagId = string.Empty;
        public string TagId { get => _tagId; set { if (_tagId == value) return; _tagId = value; OnPropertyChanged(); } }

        private string _name = string.Empty;
        public string Name { get => _name; set { if (_name == value) return; _name = value; OnPropertyChanged(); } }

        private string _vr = string.Empty;   // DICOM VR (DA/TM/PN/…)
        public string Vr { get => _vr; set { if (_vr == value) return; _vr = value; OnPropertyChanged(); } }

        private string _value = string.Empty;
        public string Value { get => _value; set { if (_value == value) return; _value = value; OnPropertyChanged(); } }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}
