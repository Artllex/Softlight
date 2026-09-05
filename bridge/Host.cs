using System;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
class Host {
    static int Main() {
        try {
            using(var input=new BinaryReader(Console.OpenStandardInput()))
            using(var output=new BinaryWriter(Console.OpenStandardOutput())) {
                while(true) {
                    int length;try{length=input.ReadInt32();}catch(EndOfStreamException){return 0;}
                    if(length<2||length>4096)return 1;
                    byte[] bytes=input.ReadBytes(length);if(bytes.Length!=length)return 1;
                    bool connected=false;
                    try {using(var pipe=new NamedPipeClientStream(".","SoftlightFirefox-"+WindowsIdentity.GetCurrent().User.Value,PipeDirection.Out)) {
                        pipe.Connect(100);using(var writer=new BinaryWriter(pipe)) {writer.Write(length);writer.Write(bytes);writer.Flush();}connected=true;
                    }}catch(TimeoutException){}catch(IOException){}
                    byte[] reply=Encoding.UTF8.GetBytes(connected?"{\"connected\":true}":"{\"connected\":false}");
                    output.Write(reply.Length);output.Write(reply);output.Flush();
                }
            }
        }catch{return 1;}
    }
}
