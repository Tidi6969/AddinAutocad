DDimnotes UserData

Version: V51
Command: DDimnotes

Thu muc nay chi de luu file tham khao/bao duong, giup biet code can cac file du lieu nao.
File trong UserData KHONG duoc copy vao bin/output va KHONG phai file runtime truc tiep.

Runtime thuc te:
- AutoCAD phai tim duoc ti.dat trong Support File Search Path khi can tra du lieu ren.
- AutoCAD phai tim duoc MIS_SET.SET trong Support File Search Path neu muon dung cau hinh notes/mau/ngon ngu.
- DDimnotes_settings.ini phai dat trong thu muc DUser thuoc AutoCAD Support File Search Path.
- Runtime KHONG tim lang.dat ngoai o dia.

File tham khao kem theo:
- ti.dat: du lieu mac dinh ren, doc section M_SCREW de lay pitch va duong kinh ren.
  Theo code mau DScrew: token[0]=size, token[1]=pitch, token[3]=h_high/duong kinh danh nghia.
- MIS_SET.SET: file mau cho DNOTES/Notes.
  Dong 7  = Pitch toggle, 0/1.
  Dong 26 = Mau chu notes.
  Dong 27 = Mau duong gach notes.
  Dong 28 = Ngon ngu. Gia tri X se chon block Lang_X(...) trong embedded lang.dat.
             Quy uoc moi: Lang_0(English_En) la fallback co dinh.
  Dong 59 = Width factor/tile chu notes.
  Dong 66 = Bat/tat desc, 0/1.
  Dong 72 = Mau mark notes.
- lang.dat: du lieu ngon ngu build-time. File nay duoc khai bao EmbeddedResource trong .csproj va duoc nhung vao DLL khi build.
- Quy_chuan_xu_ly_da_ngon_ngu_AutoCAD_CSharp_BuildTime.txt: quy chuan xu ly da ngon ngu dang ap dung cho V41.

DDimnotes_settings.ini
- File runtime phai dat trong thu muc DUser thuoc AutoCAD Support File Search Path.
- File trong UserData chi la mau tham khao, khong copy vao bin/output.
- Dinh dang runtime la thuan so tung dong theo thu tu:
  0: Mode index, 0=D, 1=B, 2=A
  1: Angle index, 0=0, 1=90, 2=180, 3=270
  2: CreateNotes, 0/1
  3: CreateMarks, 0/1
  4: NoteWidthFactor
  5: NoteTextColor ACI
  6: SideViewDistanceScale
  7: DimOffsetScale
  8: NotePosition index, 0=Xmax,Ymax, 1=Xmin,Ymin
  9: NoteOffsetXScale
  10: NoteOffsetYScale
  11: DimTextHeight, co chu dim mac dinh cua lenh DDimnotes.
      Neu thieu dong nay, code dung DIMTXT hien hanh cua AutoCAD.

DDimnotes V39 language rule:
- Build-time: UserData/lang.dat -> EmbeddedResource -> nhung vao DLL.
- Runtime: DDimnotes chi doc MIS_SET.SET line 28 de chon Lang_X trong embedded lang.dat.
- Muon them ngon ngu moi: sua UserData/lang.dat, them block Lang_X(FullName_ShortName), build lai DLL, sau do dat MIS_SET.SET line 28 = X.
- Khong dat lang.dat ngoai Support File Search Path de runtime doc nua.

DDimnotes V38 update:
- UserData/lang.dat da du 4 ngon ngu: Lang_0 English, Lang_1 Vietnamese, Lang_2 Chinese, Lang_3 Japan.
- 4 block ngon ngu co cung danh sach key hien tai de tranh fallback tung dong khong can thiet.

DDimnotes V39 update:
- Luu co chu dim nhap truc tiep o prompt vao DUser\DDimnotes_settings.ini dong 11.
- Lan chay sau, prompt dung gia tri da luu lam mac dinh thay vi luon lay DIMTXT hien hanh.
- Neu file settings cu thieu dong 11, code van tu fallback ve DIMTXT hien hanh de tuong thich.

DDimnotes V40 update:
- Them mode N = Khong tao Notes.
- Mode N van tao hinh chieu canh va dim theo logic mode D, nhung khong tao bang Notes va khong tao Note marks.
- UserData/lang.dat da them key Form_ModeNoNotes du 4 ngon ngu.
- DDimnotes_settings.ini dong 1 ho tro them gia tri 3 = N. Cac gia tri cu giu nguyen: 0=D, 1=B, 2=A.

DDimnotes V41 update:
- Them logic rieng cho set_screwBSPR.
- set_screw doc bang MSW trong ti.dat.
- Lo loi/taro tinh theo pitch/core dia luon ve xuyen suot chieu day.
- Duong kinh danh nghia M chi ve theo depth; depth am ve tu mat sau.
- Day doan ren dung goc nhu ren binh thuong, khong dung mat phang bac.
- Them tag BSPR de Notes khong bi nhan nham SPR/Spring.

DDimnotes V47 update:
- Built from accepted V41 baseline.
- set_screwBSPR: remove long thread-root extend on the nominal diameter segment.
- Nominal diameter stops at depth and only uses a short taper into the core hole.

V49:
- set_screwBSPR: remove extra separate small through-hole rectangle.
- Keep nominal diameter drawn like m_screw.
- Avoid drawing three visible layers for set_screw.

DDimnotes V87 update:
- DDimnotes_settings.ini line 58 = NoteWireHole, 0/1.
- DDimnotes_settings.ini line 59 = NoteThreadPitch/Buoc ren, 0/1.
