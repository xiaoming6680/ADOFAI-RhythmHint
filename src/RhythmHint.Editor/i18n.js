/* 手序编辑器的界面文字：简中、繁中、英、韩、日（和 Mod 一样）。键是英文标识，{0} {1} 是占位符。
 * 新加一句要五种语言都写（i18n.test.cjs 检查：句句齐全、占位符一致）。 */
(function (root, factory) {
  const api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  else root.EditorI18n = api;
})(typeof globalThis !== 'undefined' ? globalThis : this, function () {
  'use strict';

  const LANGUAGES = [['zh-Hans', '简体中文'], ['zh-Hant', '繁體中文'], ['en', 'English'], ['ko', '한국어'], ['ja', '日本語']];

  // 每一项：[简中, 繁中, 英, 韩, 日]
  const S = {
    'brand.sub': ['手序编辑器', '手序編輯器', 'Fingering Editor', '운지 편집기', '運指エディター'],
    'top.undo': ['撤销（Ctrl+Z）', '復原（Ctrl+Z）', 'Undo (Ctrl+Z)', '실행 취소 (Ctrl+Z)', '元に戻す（Ctrl+Z）'],
    'top.redo': ['重做（Ctrl+Shift+Z）', '重做（Ctrl+Shift+Z）', 'Redo (Ctrl+Shift+Z)', '다시 실행 (Ctrl+Shift+Z)', 'やり直し（Ctrl+Shift+Z）'],
    'top.saveAs': ['另存为文件', '另存為檔案', 'Save as file', '파일로 저장', 'ファイルとして保存'],
    'top.save': ['保存到游戏', '儲存到遊戲', 'Save to game', '게임에 저장', 'ゲームに保存'],
    'top.unsaved': ['有未保存的修改', '有未儲存的修改', 'Unsaved changes', '저장하지 않은 변경 사항', '未保存の変更があります'],
    'top.language': ['界面语言', '介面語言', 'Language', '언어', '表示言語'],
    'doc.untitled': ['未打开手序', '未開啟手序', 'No fingering open', '열린 운지 없음', '運指が開かれていません'],
    'doc.check': ['{0} 处待检查', '{0} 處待檢查', '{0} to review', '검토할 곳 {0}', '要確認 {0} か所'],
    'doc.song': ['曲目', '曲目', 'Song', '곡', '曲'],

    'rail.file': ['文件', '檔案', 'File', '파일', 'ファイル'],
    'rail.refs': ['参考', '參考', 'Reference', '참고', '参考'],
    'rail.audio': ['音乐', '音樂', 'Music', '음악', '音楽'],
    'rail.view': ['显示', '顯示', 'View', '보기', '表示'],
    'rail.keys': ['快捷键', '快捷鍵', 'Shortcuts', '단축키', 'ショートカット'],
    'rail.history': ['历史', '歷史', 'History', '기록', '履歴'],
    'rail.about': ['关于', '關於', 'About', '정보', '情報'],

    'history.empty': ['暂无记录', '暫無紀錄', 'No levels yet', '기록 없음', '記録なし'],
    'history.copyPage': ['复制本页地址', '複製本頁網址', 'Copy page address', '페이지 주소 복사', 'ページのアドレスをコピー'],
    'history.unsaved': ['{0} 处未保存', '{0} 處未儲存', '{0} unsaved', '미저장 {0}곳', '未保存 {0} か所'],
    'history.saved': ['已保存', '已儲存', 'Saved', '저장됨', '保存済み'],
    'history.missing': ['找不到该关卡的数据，请在游戏中重新打开。', '找不到該關卡的資料，請在遊戲中重新開啟。', "This level's data is missing. Open it from the game again.", '이 레벨의 데이터를 찾을 수 없습니다. 게임에서 다시 여세요.', 'このレベルのデータが見つかりません。ゲームから開き直してください。'],
    'history.tip': ['修改已自动保留。收藏本页（Ctrl+D）即可在游戏外打开。', '修改已自動保留。收藏本頁（Ctrl+D）即可在遊戲外開啟。', 'Changes are saved automatically. Bookmark this page (Ctrl+D) to open it without the game.', '수정 내용은 자동으로 보존됩니다. 이 페이지를 즐겨찾기(Ctrl+D)하면 게임 없이 열 수 있습니다.', '変更は自動で保持されます。このページをブックマーク（Ctrl+D）するとゲーム外でも開けます。'],
    'history.gotIt': ['确定', '確定', 'OK', '확인', 'OK'],
    'history.show': ['查看历史', '查看歷史', 'View history', '기록 보기', '履歴を表示'],

    'about.author': ['作者', '作者', 'Author', '제작자', '作者'],
    'about.version': ['版本 {0}', '版本 {0}', 'Version {0}', '버전 {0}', 'バージョン {0}'],
    'about.contributor': ['贡献者', '貢獻者', 'Contributor', '기여자', '貢献者'],
    'about.credits': ['制作人员', '製作人員', 'Credits', '제작', 'クレジット'],
    'about.links': ['链接', '連結', 'Links', '링크', 'リンク'],
    'about.repo': ['GitHub 仓库', 'GitHub 倉庫', 'GitHub repository', 'GitHub 저장소', 'GitHub リポジトリ'],
    'about.qq': ['QQ 交流群', 'QQ 交流群', 'QQ group', 'QQ 그룹', 'QQ グループ'],

    'file.song': ['曲名', '曲名', 'Song', '곡명', '曲名'],
    'file.artist': ['曲师', '曲師', 'Artist', '작곡가', 'アーティスト'],
    'file.charter': ['谱师', '譜師', 'Charter', '채보자', '譜面作者'],
    'file.keys': ['键数', '鍵數', 'Keys', '키 수', 'キー数'],
    'file.keysSwitch': ['切换键数。各键数的手序分别保存，未完成的修改将保留', '切換鍵數。各鍵數的手序分別儲存，未完成的修改將保留', 'Switch the key count. Each key count is saved as a separate file, and unsaved edits are kept', '키 수를 전환합니다. 키 수마다 별도로 저장되며 저장하지 않은 편집 내용은 유지됩니다', 'キー数を切り替えます。キー数ごとに別ファイルで保存され、未保存の編集内容は保持されます'],
    'file.keysFixed': ['手序文件的键数不可切换；仅从游戏打开的关卡支持切换键数', '手序檔案的鍵數不可切換；僅從遊戲開啟的關卡支援切換鍵數', 'The key count of a fingering file cannot be changed; switching is available only for levels opened from the game', '운지 파일의 키 수는 바꿀 수 없습니다. 게임에서 연 레벨에서만 전환할 수 있습니다', '運指ファイルのキー数は変更できません。ゲームから開いたレベルでのみ切り替えられます'],
    'file.author': ['手序作者', '手序作者', 'Fingering author', '운지 작성자', '運指の作者'],
    'file.authorHint': ['分享时显示的署名', '分享時顯示的署名', 'Name shown when shared', '공유 시 표시할 이름', '共有時に表示する名前'],
    'file.comment': ['说明', '說明', 'Notes', '설명', '説明'],
    'file.commentHint': ['例如：适用的玩家或打法', '例如：適用的玩家或打法', 'e.g. intended players or play style', '예: 대상 플레이어나 플레이 스타일', '例：対象のプレイヤーや方針'],
    'file.name': ['文件名', '檔名', 'File name', '파일 이름', 'ファイル名'],
    'file.folder': ['保存位置', '儲存位置', 'Save location', '저장 위치', '保存先'],
    'file.changeFolder': ['更换保存位置', '更換儲存位置', 'Change save location', '저장 위치 변경', '保存先を変更'],
    'file.copy': ['复制', '複製', 'Copy', '복사', 'コピー'],
    'file.copied': ['已复制', '已複製', 'Copied', '복사됨', 'コピーしました'],
    'file.open': ['打开手序文件', '開啟手序檔案', 'Open fingering file', '운지 파일 열기', '運指ファイルを開く'],
    'file.statNotes': ['音符', '音符', 'Notes', '노트', 'ノーツ'],
    'file.statChanged': ['已修改', '已修改', 'Changed', '수정', '変更'],
    'file.statAuto': ['自动补齐', '自動補齊', 'Auto-filled', '자동 보완', '自動補完'],
    'file.statJacks': ['连按', '連按', 'Jacks', '연타', '連打'],
    'file.title': ['手序信息', '手序資訊', 'Fingering', '운지 정보', '運指情報'],
    'file.level': ['曲目信息', '曲目資訊', 'Song', '곡 정보', '曲情報'],

    'refs.desc': ['两侧为「标准」与「交替优先」的自动排键结果，与当前手序不同之处会高亮显示。', '兩側為「標準」與「交替優先」的自動排鍵結果，與目前手序不同之處會醒目顯示。', 'The side columns show the Standard and Alternate first auto fingerings; differences from the current fingering are highlighted.', '양옆 열은 표준과 교대 우선 자동 운지 결과이며, 현재 운지와 다른 곳이 강조됩니다.', '左右の列は「標準」と「交互優先」の自動運指です。現在の運指と異なる箇所が強調表示されます。'],
    'refs.show': ['显示此列', '顯示此列', 'Show column', '열 표시', '列を表示'],
    'refs.applySel': ['套用到所选', '套用到所選', 'Apply to selection', '선택에 적용', '選択に適用'],
    'refs.applyAll': ['套用到全曲', '套用到全曲', 'Apply to whole song', '곡 전체에 적용', '曲全体に適用'],
    'refs.confirmAll': ['将「{0}」套用到全曲？已修改的音符将被替换（可撤销）。', '將「{0}」套用到全曲？已修改的音符將被替換（可復原）。', 'Apply {0} to the whole song? Edited notes will be replaced (undoable).', '곡 전체에 {0}을(를) 적용할까요? 수정한 노트가 바뀝니다(실행 취소 가능).', '曲全体に「{0}」を適用しますか？変更したノーツは置き換えられます（元に戻せます）。'],
    'refs.width': ['参考列宽度', '參考列寬度', 'Reference column width', '참고 열 너비', '参考列の幅'],
    'refs.none': ['单独打开的文件没有自动排键参考。', '單獨開啟的檔案沒有自動排鍵參考。', 'Files opened on their own have no auto fingering reference.', '파일로 연 경우 자동 운지 참고가 없습니다.', 'ファイルから開いた場合、自動運指の参考はありません。'],

    'audio.none': ['无音乐', '無音樂', 'No music', '음악 없음', '音楽なし'],
    'audio.load': ['载入音乐', '載入音樂', 'Load music', '음악 불러오기', '音楽を読み込む'],
    'audio.fromGame': ['来自游戏的音乐', '來自遊戲的音樂', 'Music from the game', '게임의 음악', 'ゲームの音楽'],
    'audio.offset': ['音乐偏移（毫秒）', '音樂偏移（毫秒）', 'Music offset (ms)', '음악 오프셋 (ms)', '音楽オフセット（ミリ秒）'],
    'audio.offsetHint': ['声音偏早时调小，画面偏早时调大', '聲音偏早時調小，畫面偏早時調大', 'Lower it if the sound is early, raise it if the picture is early', '소리가 빠르면 줄이고, 화면이 빠르면 늘리세요', '音が早ければ下げ、映像が早ければ上げます'],
    'audio.calibrated': ['已应用游戏画面校准 {0} 毫秒', '已套用遊戲畫面校準 {0} 毫秒', 'Game visual offset applied ({0} ms)', '게임 화면 오프셋 {0}ms 적용됨', 'ゲームの映像オフセット {0} ms を適用済み'],
    'audio.volume': ['音量', '音量', 'Volume', '볼륨', '音量'],
    'audio.hitsound': ['打拍声', '打拍聲', 'Hit sound', '타격음', '打鍵音'],
    'audio.noSeek': ['该音乐文件无法跳转，只能从头播放。请从游戏重新打开编辑器，或在「音乐」中手动载入歌曲。', '該音樂檔無法跳轉，只能從頭播放。請從遊戲重新開啟編輯器，或在「音樂」中手動載入歌曲。', "This music file can't seek and only plays from the start. Reopen the editor from the game, or load the song under Music.", '이 음악 파일은 위치 이동이 안 되어 처음부터만 재생됩니다. 게임에서 편집기를 다시 열거나 음악에서 곡을 불러오세요.', 'この音楽ファイルは位置移動ができず、最初からのみ再生されます。ゲームからエディターを開き直すか、「音楽」で曲を読み込んでください。'],
    'audio.failed': ['音乐无法播放：{0}', '音樂無法播放：{0}', 'Music cannot play: {0}', '음악을 재생할 수 없습니다: {0}', '音楽を再生できません：{0}'],

    'view.style': ['音符样式', '音符樣式', 'Note style', '노트 모양', 'ノーツの形'],
    'view.styleGame': ['与游戏一致', '與遊戲一致', 'Same as in game', '게임과 동일', 'ゲームと同じ'],
    'view.styleBar': ['横条', '橫條', 'Bars', '막대', 'バー'],
    'view.styleRound': ['圆形', '圓形', 'Round', '원형', '丸'],
    'view.beats': ['节拍线', '節拍線', 'Beat lines', '박자선', '拍線'],
    'view.wave': ['波形', '波形', 'Waveform', '파형', '波形'],
    'view.gaps': ['间隔标注', '間隔標註', 'Gap labels', '간격 표시', '間隔の表示'],
    'view.ghost': ['显示原位置', '顯示原位置', 'Show original lanes', '원래 위치 표시', '元の位置を表示'],
    'view.track': ['轨道预览', '軌道預覽', 'Track preview', '트랙 미리보기', 'トラックプレビュー'],
    'view.experimental': ['试验性', '試驗性', 'Experimental', '시험적', '試験的'],
    'view.jack': ['连按提醒（毫秒）', '連按提醒（毫秒）', 'Jack warning (ms)', '연타 경고 (ms)', '連打の警告（ミリ秒）'],
    'view.jackHint': ['同一按键在此时间内连按两次时标红', '同一按鍵在此時間內連按兩次時標紅', 'Marks the same key pressed twice within this time', '같은 키를 이 시간 안에 두 번 누르면 빨갛게 표시', '同じキーをこの時間内に 2 回押すと赤く表示'],

    'keys.custom': ['键位设置', '鍵位設定', 'Key bindings', '키 설정', 'キー設定'],
    'keys.customHint': ['点击轨道后按下新键，与其他轨道重复时自动互换。默认与游戏键位一致。', '點擊軌道後按下新鍵，與其他軌道重複時自動互換。預設與遊戲鍵位一致。', 'Click a lane, then press the new key; lanes that clash swap keys. Defaults to your in-game keys.', '레인을 클릭한 뒤 새 키를 누르세요. 겹치는 레인과는 서로 바뀝니다. 기본값은 게임 키와 같습니다.', 'レーンをクリックして新しいキーを押してください。重なったレーンとは入れ替わります。初期値はゲームのキーと同じです。'],
    'keys.press': ['按下新键…', '按下新鍵…', 'Press a key…', '새 키를 누르세요…', '新しいキーを押して…'],
    'keys.laneN': ['第 {0} 轨', '第 {0} 軌', 'Lane {0}', '{0}번 레인', '{0} レーン目'],
    'keys.reset': ['恢复游戏键位', '恢復遊戲鍵位', 'Reset to in-game keys', '게임 키로 되돌리기', 'ゲームのキーに戻す'],
    'keys.clash': ['轨道键 {0} 与编辑器快捷键重复，按下时将改键。对应功能请使用界面上的按钮。', '軌道鍵 {0} 與編輯器快捷鍵重複，按下時將改鍵。對應功能請使用介面上的按鈕。', 'Lane keys {0} are also editor shortcuts; pressing them changes the lane. Use the on-screen buttons for those functions.', '레인 키 {0}이(가) 편집기 단축키와 겹쳐 누르면 레인이 바뀝니다. 해당 기능은 화면의 버튼을 사용하세요.', 'レーンキー {0} はエディターのショートカットと重複しているため、押すとレーンが変わります。該当する機能は画面のボタンを使ってください。'],
    'keys.autoNext': ['按键修改后跳到下一个音符', '按鍵修改後跳到下一個音符', 'Jump to the next note after setting a key', '키 변경 후 다음 노트로 이동', 'キーで変更後、次のノーツへ移動'],
    'keys.play': ['播放 / 暂停', '播放 / 暫停', 'Play / pause', '재생 / 일시 정지', '再生 / 一時停止'],
    'keys.lane': ['移到该轨道', '移到該軌道', 'Move to lane', '레인으로 이동', 'レーンへ移動'],
    'keys.shift': ['左右移动一轨', '左右移動一軌', 'Shift one lane', '한 레인 이동', '1 レーン移動'],
    'keys.mirror': ['左右镜像', '左右鏡像', 'Mirror', '좌우 반전', '左右反転'],
    'keys.undo': ['撤销 / 重做', '復原 / 重做', 'Undo / redo', '실행 취소 / 다시 실행', '元に戻す / やり直し'],
    'keys.save': ['保存到游戏', '儲存到遊戲', 'Save to game', '게임에 저장', 'ゲームに保存'],
    'keys.home': ['回到开头', '回到開頭', 'Back to start', '처음으로', '先頭へ'],
    'keys.next': ['下一处待检查', '下一處待檢查', 'Next to review', '다음 검토할 곳', '次の要確認箇所'],
    'keys.all': ['全选 / 取消选择', '全選 / 取消選取', 'Select all / clear', '모두 선택 / 선택 해제', 'すべて選択 / 選択解除'],
    'keys.scroll': ['滚动时间轴', '捲動時間軸', 'Scroll the timeline', '타임라인 스크롤', 'タイムラインをスクロール'],
    'keys.zoom': ['纵向缩放', '縱向縮放', 'Zoom', '세로 확대 / 축소', '縦方向の拡大縮小'],
    'keys.pan': ['拖动画面', '拖動畫面', 'Pan', '화면 끌기', '画面をドラッグ'],
    'keys.box': ['框选（Shift 加选）', '框選（Shift 加選）', 'Box select (Shift adds)', '드래그 선택 (Shift로 추가)', '範囲選択（Shift で追加）'],
    'keys.drag': ['拖动音符换轨', '拖動音符換軌', 'Drag a note to change lane', '노트를 끌어 레인 변경', 'ノーツをドラッグしてレーン変更'],
    'keys.loop': ['循环起点 / 终点', '循環起點 / 終點', 'Loop start / end', '반복 시작 / 끝', 'ループ開始 / 終了'],
    'keys.wheel': ['滚轮', '滾輪', 'Wheel', '휠', 'ホイール'],
    'keys.emptyDrag': ['在空白处或时间尺上拖动', '在空白處或時間尺上拖動', 'Drag on empty space or the time ruler', '빈 곳이나 시간 눈금에서 드래그', '空白か時間目盛りでドラッグ'],
    'keys.spaceDrag': ['空格 + 拖动 / 中键', '空白鍵 + 拖動 / 中鍵', 'Space + drag / middle button', 'Space + 드래그 / 가운데 버튼', 'Space + ドラッグ / 中ボタン'],

    'insp.none': ['选择音符', '選擇音符', 'Select notes', '노트 선택', 'ノーツを選択'],
    'insp.noneHint': ['拖动音符换轨，或按 {0} 修改。', '拖動音符換軌，或按 {0} 修改。', 'Drag a note to another lane, or press {0}.', '노트를 다른 레인으로 끌거나 {0}을(를) 누르세요.', 'ノーツを別のレーンへドラッグするか、{0} を押してください。'],
    'insp.count': ['选中 {0} 个音符', '選取 {0} 個音符', '{0} notes selected', '노트 {0}개 선택', '{0} 個のノーツを選択'],
    'insp.time': ['时间', '時間', 'Time', '시간', '時間'],
    'insp.tile': ['砖', '磚', 'Tile', '타일', 'タイル'],
    'insp.gap': ['间隔', '間隔', 'Gap', '간격', '間隔'],
    'insp.gapValue': ['前 {0} · 后 {1}', '前 {0} · 後 {1}', 'prev {0} · next {1}', '앞 {0} · 뒤 {1}', '前 {0}・後 {1}'],
    'insp.kind': ['类型', '類型', 'Type', '종류', '種類'],
    'kind.1': ['单押', '單押', 'Single', '단일', '単押し'],
    'kind.2': ['双押', '雙押', 'Double', '2키 동시', '2 つ押し'],
    'kind.3': ['三押', '三押', 'Triple', '3키 동시', '3 つ押し'],
    'kind.4': ['四押', '四押', 'Quad', '4키 동시', '4 つ押し'],
    'kind.5': ['五押', '五押', '5 keys', '5키 동시', '5 つ押し'],
    'kind.6': ['六押', '六押', '6 keys', '6키 동시', '6 つ押し'],
    'kind.7': ['七押', '七押', '7 keys', '7키 동시', '7 つ押し'],
    'kind.8': ['八押', '八押', '8 keys', '8키 동시', '8 つ押し'],
    'insp.source': ['来源', '來源', 'Source', '출처', '出所'],
    'source.custom': ['专属手序', '專屬手序', 'Custom fingering', '전용 운지', '専用運指'],
    'source.auto': ['自动补齐', '自動補齊', 'Auto-filled', '자동 보완', '自動補完'],
    'source.changed': ['已修改', '已修改', 'Changed', '수정됨', '変更済み'],
    'insp.lanes': ['按键', '按鍵', 'Keys', '키', 'キー'],
    'insp.lanesHint': ['多押：选择 {0} 个键', '多押：選擇 {0} 個鍵', 'Chord: pick {0} keys', '동시 입력: 키 {0}개 선택', '同時押し：キーを {0} つ選択'],
    'insp.left': ['左移', '左移', 'Left', '왼쪽', '左へ'],
    'insp.right': ['右移', '右移', 'Right', '오른쪽', '右へ'],
    'insp.mirror': ['镜像', '鏡像', 'Mirror', '반전', '反転'],
    'insp.revert': ['还原', '還原', 'Revert', '되돌리기', '元に戻す'],
    'insp.next': ['下一处待检查', '下一處待檢查', 'Next to review', '다음 검토할 곳', '次の要確認箇所'],
    'insp.autoNote': ['谱面更新后此处与原手序不一致，已暂用自动排键，请检查。', '譜面更新後此處與原手序不一致，已暫用自動排鍵，請檢查。', 'This part no longer matches the fingering after a chart update and uses auto fingering for now. Please review it.', '채보 업데이트 후 이 부분이 기존 운지와 맞지 않아 자동 운지를 사용 중입니다. 확인해 주세요.', '譜面の更新でこの部分が以前の運指と一致しないため、自動運指を使用しています。確認してください。'],
    'insp.jackNote': ['同一按键 {0} 毫秒内连按两次', '同一按鍵 {0} 毫秒內連按兩次', 'Same key twice within {0} ms', '같은 키를 {0}ms 안에 두 번', '同じキーを {0} ミリ秒以内に 2 回'],

    'tr.play': ['播放（空格）', '播放（空白鍵）', 'Play (Space)', '재생 (Space)', '再生（Space）'],
    'tr.pause': ['暂停（空格）', '暫停（空白鍵）', 'Pause (Space)', '일시 정지 (Space)', '一時停止（Space）'],
    'tr.start': ['回到开头（Home）', '回到開頭（Home）', 'Back to start (Home)', '처음으로 (Home)', '先頭へ（Home）'],
    'tr.speed': ['播放速度', '播放速度', 'Playback speed', '재생 속도', '再生速度'],
    'tr.loopA': ['设为循环起点（[）', '設為循環起點（[）', 'Set loop start ([)', '반복 시작 지정 ([)', 'ループ開始に設定（[）'],
    'tr.loopB': ['设为循环终点（]）', '設為循環終點（]）', 'Set loop end (])', '반복 끝 지정 (])', 'ループ終了に設定（]）'],
    'tr.volume': ['音乐音量', '音樂音量', 'Music volume', '음악 볼륨', '音楽の音量'],
    'tr.loop': ['循环播放', '循環播放', 'Loop', '반복 재생', 'ループ再生'],

    'zoom.in': ['放大', '放大', 'Zoom in', '확대', '拡大'],
    'zoom.out': ['缩小', '縮小', 'Zoom out', '축소', '縮小'],
    'zoom.label': ['缩放', '縮放', 'Zoom', '배율', 'ズーム'],
    'zoom.slider': ['拖动或滚动鼠标滚轮调整缩放', '拖動或滾動滑鼠滾輪調整縮放', 'Drag or scroll to zoom', '드래그하거나 휠로 확대/축소', 'ドラッグまたはホイールでズーム'],
    'track.zoomHint': ['滚轮缩放视角，双击还原', '滾輪縮放視角，雙擊還原', 'Scroll to zoom, double-click to reset', '휠로 확대/축소, 더블클릭으로 초기화', 'ホイールでズーム、ダブルクリックでリセット'],
    'track.expand': ['放大窗口', '放大視窗', 'Enlarge', '창 크게', 'ウィンドウを大きく'],
    'track.shrink': ['缩小窗口', '縮小視窗', 'Shrink', '창 작게', 'ウィンドウを小さく'],
    'zoom.fit': ['整首', '整首', 'Whole song', '곡 전체', '曲全体'],
    'map.title': ['全曲概览（点击或拖动以跳转）', '全曲概覽（點擊或拖動以跳轉）', 'Overview (click or drag to jump)', '전체 보기(클릭하거나 끌어서 이동)', '全体表示（クリックまたはドラッグで移動）'],

    'empty.title': ['未打开手序', '未開啟手序', 'No fingering open', '열린 운지 없음', '運指が開かれていません'],
    'empty.body': ['在游戏中按 F8 → 专属手序 → 编辑本关手序即可打开当前关卡；也可从左侧历史继续编辑，或打开他人分享的手序文件。', '在遊戲中按 F8 → 專屬手序 → 編輯本關手序即可開啟目前關卡；也可從左側歷史繼續編輯，或開啟他人分享的手序檔案。', "In the game, press F8 → Custom fingering → Edit this level's fingering to open the current level. You can also continue from History on the left or open a shared fingering file.", '게임에서 F8 → 전용 운지 → 이 레벨 운지 편집을 누르면 현재 레벨이 열립니다. 왼쪽 기록에서 이어서 편집하거나 공유받은 운지 파일을 열 수도 있습니다.', 'ゲームで F8 → 専用運指 → このレベルの運指を編集 を押すと現在のレベルが開きます。左の履歴から再開したり、共有された運指ファイルを開いたりすることもできます。'],

    'col.current': ['当前手序', '目前手序', 'Current', '현재 운지', '現在の運指'],
    'col.standard': ['标准', '標準', 'Standard', '표준', '標準'],
    'col.steady': ['按拍分手', '按拍分手', 'Hands by beat', '박자별 손 배정', '拍で手を分ける'],
    'col.alternate': ['交替优先', '交替優先', 'Alternate first', '교대 우선', '交互優先'],
    'col.left': ['左手', '左手', 'Left', '왼손', '左手'],
    'col.right': ['右手', '右手', 'Right', '오른손', '右手'],
    'gap.jack': ['连按', '連按', 'jack', '연타', '連打'],

    'save.pickTitle': ['选择手序文件夹', '選擇手序資料夾', 'Choose the fingering folder', '운지 폴더 선택', '運指フォルダーを選択'],
    'save.pickBody': ['首次保存需选择游戏的手序文件夹，之后将直接保存至：', '首次儲存需選擇遊戲的手序資料夾，之後將直接儲存至：', "Choose the game's fingering folder once; later saves go straight to:", '처음 한 번 게임의 운지 폴더를 선택하면 이후에는 바로 저장됩니다:', '初回のみゲームの運指フォルダーを選択してください。以後は直接保存されます：'],
    'save.pickBodyInbox': ['游戏位于系统文件夹，浏览器无法直接写入。首次保存请选择下方文件夹，游戏会自动将手序移入手序文件夹：', '遊戲位於系統資料夾，瀏覽器無法直接寫入。首次儲存請選擇下方資料夾，遊戲會自動將手序移入手序資料夾：', 'The game is in a system folder the browser cannot write to. Choose the folder below once; the game moves your fingerings into its fingering folder automatically:', '게임이 브라우저가 쓸 수 없는 시스템 폴더에 있습니다. 처음 한 번 아래 폴더를 선택하면 게임이 운지를 자동으로 운지 폴더로 옮깁니다:', 'ゲームはブラウザーが書き込めないシステムフォルダーにあります。初回のみ下のフォルダーを選択してください。運指はゲームが自動的に運指フォルダーへ移します：'],
    'save.pickGo': ['选择文件夹', '選擇資料夾', 'Choose folder', '폴더 선택', 'フォルダーを選択'],
    'save.cancel': ['取消', '取消', 'Cancel', '취소', 'キャンセル'],
    'save.done': ['已保存：{0}，游戏内立即生效。', '已儲存：{0}，遊戲內立即生效。', 'Saved {0}. It takes effect in the game immediately.', '{0} 저장됨. 게임에 즉시 적용됩니다.', '{0} を保存しました。ゲームに即時反映されます。'],
    'save.downloaded': ['已下载 {0}，请放入：{1}', '已下載 {0}，請放入：{1}', 'Downloaded {0}. Put it in: {1}', '{0}을(를) 내려받았습니다. 다음 폴더에 넣으세요: {1}', '{0} をダウンロードしました。次のフォルダーに入れてください：{1}'],
    'save.wrongFolder': ['所选文件夹为「{0}」，不是手序文件夹 Fingerings。请重新选择：', '所選資料夾為「{0}」，不是手序資料夾 Fingerings。請重新選擇：', 'The chosen folder is "{0}", not the Fingerings folder. Please choose:', '선택한 폴더는 "{0}"(으)로, 운지 폴더 Fingerings가 아닙니다. 다음 폴더를 다시 선택해 주세요:', '選択したフォルダーは「{0}」で、運指フォルダー Fingerings ではありません。次のフォルダーを選び直してください：'],
    'save.pickAgain': ['重新选择', '重新選擇', 'Choose again', '다시 선택', '選び直す'],
    'save.folderLost': ['无法写入上次选择的手序文件夹（已移动、删除或没有权限）。请重新选择：', '無法寫入上次選擇的手序資料夾（已移動、刪除或沒有權限）。請重新選擇：', "Can't write to the fingering folder chosen last time (moved, deleted, or no permission). Please choose it again:", '지난번에 선택한 운지 폴더에 쓸 수 없습니다(이동, 삭제 또는 권한 없음). 다시 선택해 주세요:', '前回選択した運指フォルダーに書き込めません（移動・削除されたか、権限がありません）。選び直してください：'],
    'save.folderSet': ['保存位置已更换为 {0}。', '儲存位置已更換為 {0}。', 'Save location changed to {0}.', '저장 위치를 {0}(으)로 변경했습니다.', '保存先を {0} に変更しました。'],
    'save.renamed': ['已保存为 {0}：文件夹里的同名文件是另一首谱的手序，未覆盖。', '已儲存為 {0}：資料夾裡的同名檔案是另一首譜的手序，未覆蓋。', 'Saved as {0}: the file with the same name in the folder is a fingering for another chart and was kept.', '{0}(으)로 저장했습니다. 폴더에 있는 같은 이름의 파일은 다른 채보의 운지라서 덮어쓰지 않았습니다.', '{0} として保存しました。フォルダー内の同名ファイルは別の譜面の運指のため、上書きしていません。'],
    'save.tooMany': ['文件夹里已有太多名为 {0} 的其他手序，未保存。请在文件面板修改文件名。', '資料夾裡已有太多名為 {0} 的其他手序，未儲存。請在檔案面板修改檔名。', 'Not saved: the folder already has too many other fingerings named {0}. Change the file name in the File panel.', '저장하지 않았습니다. 폴더에 {0} 이름의 다른 운지가 너무 많습니다. 파일 패널에서 파일 이름을 바꿔 주세요.', '保存していません。フォルダーに {0} という名前の別の運指が多すぎます。ファイルパネルでファイル名を変更してください。'],
    'save.failed': ['保存失败：{0}', '儲存失敗：{0}', 'Save failed: {0}', '저장 실패: {0}', '保存に失敗しました：{0}'],
    'saveAs.done': ['已下载 {0}，可用于分享。', '已下載 {0}，可用於分享。', 'Downloaded {0} for sharing.', '{0}을(를) 내려받았습니다. 공유할 수 있습니다.', '{0} をダウンロードしました。共有に使えます。'],

    'draft.restored': ['已恢复上次的进度（{0} 处未保存）。', '已恢復上次的進度（{0} 處未儲存）。', 'Restored your last session ({0} unsaved).', '지난 진행 상황을 복원했습니다(미저장 {0}곳).', '前回の続きを復元しました（未保存 {0} か所）。'],
    'draft.restoredSaved': ['已恢复上次保存的进度（与本次导出相差 {0} 处）。', '已恢復上次儲存的進度（與本次匯出相差 {0} 處）。', 'Restored your last saved progress ({0} notes differ from this export).', '지난번 저장한 상태를 복원했습니다(이번 내보내기와 {0}곳 다름).', '前回保存した状態を復元しました（今回の書き出しと {0} か所異なります）。'],
    'draft.discard': ['放弃', '放棄', 'Discard', '버리기', '破棄'],
    'draft.otherTab': ['本关已在另一个标签页中修改。为避免互相覆盖，本页暂停自动保留进度。', '本關已在另一個分頁中修改。為避免互相覆蓋，本頁暫停自動保留進度。', 'This level was changed in another tab. To avoid overwriting each other, this tab has stopped keeping your progress.', '이 레벨이 다른 탭에서 수정되었습니다. 서로 덮어쓰지 않도록 이 탭은 진행 상황 자동 보존을 멈췄습니다.', 'このレベルは別のタブで変更されました。上書きし合わないよう、このタブでは進行状況の自動保持を停止しています。'],
    'draft.useOther': ['载入另一页的进度', '載入另一頁的進度', "Load the other tab's progress", '다른 탭의 진행 상황 불러오기', '別のタブの状態を読み込む'],
    'draft.keepMine': ['以本页为准', '以本頁為準', 'Keep this tab', '이 탭 기준으로 유지', 'このタブを優先'],
    'draft.found': ['找到上次的进度，但无法确认是在当前这份手序上修改的。是否载入？', '找到上次的進度，但無法確認是在目前這份手序上修改的。是否載入？', 'Found earlier progress, but it may not have been made on this fingering. Load it?', '이전 진행 상황이 있지만 현재 운지에서 수정한 것인지 확인할 수 없습니다. 불러올까요?', '前回の進行状況がありますが、現在の運指で編集したものか確認できません。読み込みますか？'],
    'draft.load': ['载入', '載入', 'Load', '불러오기', '読み込む'],
    'draft.skip': ['不载入', '不載入', "Don't load", '불러오지 않기', '読み込まない'],
    'draft.dropped': ['浏览器存储空间不足，已删除 {0} 个关卡较早的未保存进度。', '瀏覽器儲存空間不足，已刪除 {0} 個關卡較早的未儲存進度。', "The browser's storage was full, so older unsaved progress for {0} levels was deleted.", '브라우저 저장 공간이 부족해 레벨 {0}개의 오래된 미저장 진행 상황을 삭제했습니다.', 'ブラウザーの保存領域が不足したため、{0} レベル分の古い未保存の進行状況を削除しました。'],
    'draft.storageFull': ['浏览器存储空间不足，进度未能自动保留。请尽快保存到游戏。', '瀏覽器儲存空間不足，進度未能自動保留。請盡快儲存到遊戲。', "The browser's storage is full, so your progress could not be kept. Please save to the game soon.", '브라우저 저장 공간이 부족해 진행 상황을 보존하지 못했습니다. 게임에 저장해 주세요.', 'ブラウザーの保存領域が不足しているため、進行状況を保持できませんでした。早めにゲームに保存してください。'],
    'draft.missed': ['本关的音符分组有变化，上次改过的 {0} 处未能恢复，请检查。', '本關的音符分組有變化，上次改過的 {0} 處未能恢復，請檢查。', 'The notes in this level are grouped differently now; {0} of your earlier edits could not be restored. Please check them.', '이 레벨의 노트 묶음이 바뀌어 이전에 수정한 {0}곳을 복원하지 못했습니다. 확인해 주세요.', 'このレベルのノーツのまとまりが変わったため、前回の変更のうち {0} か所を復元できませんでした。確認してください。'],

    'toast.cannotMove': ['无法移动：目标轨道超出范围或已被占用。', '無法移動：目標軌道超出範圍或已被佔用。', "Can't move: the lane is out of range or already used.", '이동할 수 없습니다: 레인이 범위를 벗어나거나 이미 사용 중입니다.', '移動できません：レーンが範囲外か、すでに使われています。'],
    'toast.chordKey': ['多押请拖动其中一个键，或在右侧面板中选择。', '多押請拖動其中一個鍵，或在右側面板中選擇。', 'For chords, drag one key or pick keys in the right panel.', '동시 입력은 키 하나를 끌거나 오른쪽 패널에서 선택하세요.', '同時押しはキーを 1 つドラッグするか、右のパネルで選択してください。'],
    'toast.opened': ['已打开 {0}', '已開啟 {0}', 'Opened {0}', '{0}을(를) 열었습니다', '{0} を開きました'],
    'toast.keys': ['已切换至 {0}K', '已切換至 {0}K', 'Switched to {0}K', '{0}K로 전환했습니다', '{0}K に切り替えました'],
    'toast.openFailed': ['无法打开文件：{0}', '無法開啟檔案：{0}', 'Cannot open the file: {0}', '파일을 열 수 없습니다: {0}', 'ファイルを開けません：{0}'],
    'toast.tooBig': ['文件超过 {1} MB，未打开：{0}', '檔案超過 {1} MB，未開啟：{0}', '{0} was not opened: it is larger than {1} MB.', '{0}은(는) {1}MB를 넘어 열지 않았습니다.', '{0} は {1} MB を超えているため開いていません。'],
    'toast.noCheck': ['没有待检查的位置。', '沒有待檢查的位置。', 'Nothing left to review.', '검토할 곳이 없습니다.', '要確認箇所はありません。'],
    'toast.applied': ['已套用到 {0} 个音符。', '已套用到 {0} 個音符。', 'Applied to {0} notes.', '노트 {0}개에 적용했습니다.', '{0} 個のノーツに適用しました。'],
    'toast.nothing': ['没有可套用的音符（分组不同或已一致）。', '沒有可套用的音符（分組不同或已一致）。', 'Nothing to apply (different grouping, or already the same).', '적용할 노트가 없습니다(묶음이 다르거나 이미 같음).', '適用できるノーツはありません（グループが異なるか、すでに同じです）。'],
    'toast.confirmOpen': ['有未保存的修改，确定打开其他文件吗？', '有未儲存的修改，確定開啟其他檔案嗎？', 'You have unsaved changes. Open another file anyway?', '저장하지 않은 변경 사항이 있습니다. 다른 파일을 열까요?', '未保存の変更があります。別のファイルを開きますか？'],

    'err.notFingering': ['不是 RhythmHint 手序文件', '不是 RhythmHint 手序檔案', 'Not a RhythmHint fingering file', 'RhythmHint 운지 파일이 아닙니다', 'RhythmHint の運指ファイルではありません'],
    'err.tooNew': ['文件版本比编辑器新，请更新 Mod', '檔案版本比編輯器新，請更新 Mod', 'This file is newer than the editor; please update the mod', '파일 버전이 편집기보다 새롭습니다. 모드를 업데이트해 주세요', 'ファイルの版がエディターより新しいため、Mod を更新してください'],
    'err.badFile': ['文件内容有误（{0}）', '檔案內容有誤（{0}）', 'The file is damaged ({0})', '파일 내용에 오류가 있습니다({0})', 'ファイルの内容に誤りがあります（{0}）'],
    'err.notJson': ['不是有效的 JSON', '不是有效的 JSON', 'Not valid JSON', '올바른 JSON이 아닙니다', '有効な JSON ではありません'],
    'err.notSession': ['游戏导出的数据不完整', '遊戲匯出的資料不完整', 'The data exported by the game is incomplete', '게임에서 내보낸 데이터가 불완전합니다', 'ゲームが書き出したデータが不完全です'],

    'loop.tooShort': ['循环起点和终点太近，未开启循环。', '循環起點和終點太近，未開啟循環。', 'The loop start and end are too close. The loop is not turned on.', '반복 시작점과 끝점이 너무 가까워 반복을 켜지 않았습니다.', 'ループの開始点と終了点が近すぎるため、ループをオンにしていません。'],
  };

  const INDEX = { 'zh-Hans': 0, 'zh-Hant': 1, en: 2, ko: 3, ja: 4 };

  /** 浏览器语言 → 编辑器语言；认不出用英文（和 Mod 一样）。 */
  function fromBrowser(tag) {
    const t = String(tag || '').toLowerCase();
    if (t.startsWith('zh')) return /hant|tw|hk|mo/.test(t) ? 'zh-Hant' : 'zh-Hans';
    if (t.startsWith('ko')) return 'ko';
    if (t.startsWith('ja')) return 'ja';
    return 'en';
  }

  function create(language) {
    const i = INDEX[language] !== undefined ? INDEX[language] : 2;
    const t = (key, ...args) => {
      const row = S[key];
      const text = row ? row[i] : key;
      return text.replace(/\{(\d+)\}/g, (m, n) => args[n] !== undefined ? String(args[n]) : m);
    };
    t.language = Object.keys(INDEX)[i];
    return t;
  }

  return { LANGUAGES, STRINGS: S, create, fromBrowser };
});
