from io import BytesIO
from flask import Flask, request, jsonify, send_file
from pytubefix import YouTube
import re

app = Flask(__name__)

# def download_video(url, resolution):
#     try:
#         yt = YouTube(url)
        
#         # Debug: Print all available streams
#         print(f"Available progressive streams for {url}:")
#         for stream in yt.streams.filter(progressive=True, file_extension='mp4'):
#             print(f"  - {stream.resolution} - {stream.mime_type}")
        
#         stream = yt.streams.filter(progressive=True, file_extension='mp4', resolution=resolution).first()
#         if stream:
#             out_dir = f"./downloads/{url.split('v=')[1].split('&')[0]}"
#             import os
#             os.makedirs(out_dir, exist_ok=True)
#             stream.download(output_path=out_dir)
#             return True, None
#         else:
#             print(f"\nTrying non-progressive streams:")
#             for stream in yt.streams.filter(file_extension='mp4', res=resolution):
#                 print(f"  - {stream.resolution} - {stream.mime_type} - audio: {stream.includes_audio_track}")
            
#             return False, "Video with the specified resolution not found."
#     except Exception as e:
#         return False, str(e)
def download_video(url, resolution):
    try:
        yt = YouTube(url)

        stream = yt.streams.filter(
            progressive=True,
            file_extension='mp4',
            resolution=resolution
        ).first()

        if not stream:
            return None, "Video with the specified resolution not found."

        buffer = BytesIO()
        stream.stream_to_buffer(buffer)
        buffer.seek(0)

        filename = f"{yt.title}_{resolution}.mp4"

        return {
            "buffer": buffer,
            "filename": filename,
            "mime": "video/mp4"
        }, None

    except Exception as e:
        return None, str(e)
    
def download_audio(url):
    try:
        yt = YouTube(url)

        # Get best quality audio-only stream
        stream = yt.streams.filter(only_audio=True).order_by('abr').desc().first()

        if not stream:
            return None, "Audio stream not found."

        buffer = BytesIO()
        stream.stream_to_buffer(buffer)
        buffer.seek(0)

        # Decide extension & mime type
        if "webm" in stream.mime_type:
            ext = "webm"
            mime = "audio/webm"
        else:
            ext = "mp4"
            mime = "audio/mp4"

        filename = f"{yt.title}.{ext}"

        return {
            "buffer": buffer,
            "filename": filename,
            "mime": mime
        }, None

    except Exception as e:
        return None, str(e)

def get_video_info(url):
    try:
        yt = YouTube(url)
        stream = yt.streams.first()
        video_info = {
            "title": yt.title,
            "author": yt.author,
            "length": yt.length,
            "views": yt.views,
            "description": yt.description,
            "publish_date": yt.publish_date,
        }
        return video_info, None
    except Exception as e:
        return None, str(e)

def is_valid_youtube_url(url):
    pattern = r"^(https?://)?(www\.)?youtube\.com/watch\?v=[\w-]+(&\S*)?$"
    return re.match(pattern, url) is not None

# @app.route('/download/<resolution>', methods=['POST'])
# def download_by_resolution(resolution):
#     data = request.get_json()
#     url = data.get('url')
    
#     if not url:
#         return jsonify({"error": "Missing 'url' parameter in the request body."}), 400

#     if not is_valid_youtube_url(url):
#         return jsonify({"error": "Invalid YouTube URL."}), 400
    
#     success, error_message = download_video(url, resolution)
    
#     if success:
#         return jsonify({"message": f"Video with resolution {resolution} downloaded successfully."}), 200
#     else:
#         return jsonify({"error": error_message}), 500
@app.route('/download/<resolution>', methods=['POST'])
def download_by_resolution(resolution):
    data = request.get_json()
    url = data.get('url')

    if not url:
        return jsonify({"error": "Missing 'url' parameter in the request body."}), 400

    if not is_valid_youtube_url(url):
        return jsonify({"error": "Invalid YouTube URL."}), 400

    result, error_message = download_video(url, resolution)

    if result:
        return send_file(
            result["buffer"],
            mimetype=result["mime"],
            as_attachment=True,
            download_name=result["filename"]
        )

    return jsonify({"error": error_message}), 500


@app.route('/download/audio', methods=['POST'])
def download_audio_only():
    data = request.get_json()
    url = data.get('url')

    if not url:
        return jsonify({"error": "Missing 'url' parameter in the request body."}), 400

    if not is_valid_youtube_url(url):
        return jsonify({"error": "Invalid YouTube URL."}), 400

    result, error_message = download_audio(url)

    if result:
        return send_file(
            result["buffer"],
            mimetype=result["mime"],
            as_attachment=True,
            download_name=result["filename"]
        )

    return jsonify({"error": error_message}), 500


@app.route('/video_info', methods=['POST'])
def video_info():
    data = request.get_json()
    url = data.get('url')
    
    if not url:
        return jsonify({"error": "Missing 'url' parameter in the request body."}), 400

    if not is_valid_youtube_url(url):
        return jsonify({"error": "Invalid YouTube URL."}), 400
    
    video_info, error_message = get_video_info(url)
    
    if video_info:
        return jsonify(video_info), 200
    else:
        return jsonify({"error": error_message}), 500


@app.route('/available_resolutions', methods=['POST'])
def available_resolutions():
    data = request.get_json()
    url = data.get('url')
    
    if not url:
        return jsonify({"error": "Missing 'url' parameter in the request body."}), 400

    if not is_valid_youtube_url(url):
        return jsonify({"error": "Invalid YouTube URL."}), 400
    
    try:
        yt = YouTube(url)
        progressive_resolutions = list(set([
            stream.resolution 
            for stream in yt.streams.filter(progressive=True, file_extension='mp4')
            if stream.resolution
        ]))
        all_resolutions = list(set([
            stream.resolution 
            for stream in yt.streams.filter(file_extension='mp4')
            if stream.resolution
        ]))
        return jsonify({
            "progressive": sorted(progressive_resolutions),
            "all": sorted(all_resolutions)
        }), 200
    except Exception as e:
        return jsonify({"error": str(e)}), 500
    
if __name__ == '__main__':
    # app.run(debug=True)
    app.run(host="0.0.0.0", port=5000, debug=True)

