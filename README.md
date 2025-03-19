### General AI Assistant
Sends speech to an AI model

## Installation
You need to install 
- ffmpeg
- whisper-cli (or whisper)
- ollama

In the code, set the local path for your whisper model: 
```
const string ModelPath = "<model>";
```

## Syntax
### Example ffmpeg
```
ffmpeg -f avfoundation -i ":0" -t 10 output.wav
```
### Example whisper
```
whisper-cli {file} --model {model}
```
### Example ollama
```
ollama run <model> <message>
```

## To run 
```
dotnet run
```