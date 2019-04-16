var playing = undefined;

function play_preview(id, obj){
    
    if (playing != undefined) {
        var temp_id = playing.id; 
        stop_preview(); 

        if (temp_id == id){
            return;
        }
    }
    var audio = new Audio(`https://b.ppy.sh/preview/${id}.mp3`);
    
    audio.addEventListener('ended', function(){
        stop_preview();
    });
    
    playing = {
        id : id,
        player : audio,
        panel : obj,
    };

    playing.player.play();
    obj.style.backgroundImage = "url(/css/preview_stop.png)";
}

function stop_preview(){
    if (playing != undefined){
        playing.player.pause();
        playing.player.currentTime = 0;
        playing.panel.style = "";
        playing = undefined;
    }
}