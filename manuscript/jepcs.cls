\NeedsTeXFormat{LaTeX2e}
\ProvidesClass{jepcs}[2026/08/15 JEPCS journal manuscript template]

\LoadClass[letterpaper]{article}

\RequirePackage{geometry}
\geometry{
  letterpaper,
  top=0.97986in,
  bottom=0.75in,
  left=0.75in,
  right=0.75in,
  headheight=14pt,
  headsep=7pt,
  footskip=23pt
}

\RequirePackage{iftex}
\ifPDFTeX
  \RequirePackage[T1]{fontenc}
  \RequirePackage{newtxtext}
  \RequirePackage{amsmath}
  \RequirePackage{newtxmath}
\else
  \RequirePackage{fontspec}
  \IfFontExistsTF{Times New Roman}{%
    \setmainfont{Times New Roman}[Ligatures=TeX]
  }{%
    \setmainfont{Tinos}[Ligatures=TeX]
  }
  \RequirePackage{amsmath}
  \RequirePackage[noOT1]{newtxmath}
\fi

\RequirePackage{graphicx}
\RequirePackage{array}
\RequirePackage{tabularx}
\RequirePackage{booktabs}
\RequirePackage{cite}
\RequirePackage{enumitem}
\RequirePackage{titlesec}
\RequirePackage{caption}
\RequirePackage{fancyhdr}
\RequirePackage{hyperref}
\RequirePackage[table]{xcolor}
\RequirePackage{ragged2e}
\RequirePackage{etoolbox}
\RequirePackage{lastpage}

\hypersetup{
  colorlinks=true,
  linkcolor=black,
  citecolor=black,
  urlcolor=blue,
  pdfborder={0 0 0}
}
\urlstyle{same}

% Word template body: Times New Roman 10.5 pt, near-single spacing.
\AtBeginDocument{\fontsize{10.5pt}{10.95pt}\selectfont\justifying}
\setlength{\parindent}{0pt}
\setlength{\parskip}{3.3pt}
\hyphenpenalty=10000
\exhyphenpenalty=10000
\tolerance=1600
\emergencystretch=1.5em
\clubpenalty=10000
\widowpenalty=10000

% Section appearance (number is included manually in the sample to match Word exactly).
\titleformat{\section}{\normalfont\bfseries\fontsize{11.5pt}{11.5pt}\selectfont}{}{0pt}{}
\titlespacing*{\section}{0pt}{10pt}{4pt}
\titleformat{\subsection}{\normalfont\bfseries\fontsize{10.5pt}{10.5pt}\selectfont}{}{0pt}{}
\titlespacing*{\subsection}{0pt}{7pt}{3pt}
\setcounter{secnumdepth}{0}

% Compact bullets matching the Word template.
\setlist[itemize]{leftmargin=0.18in,labelsep=0.06in,itemsep=1pt,topsep=1pt,parsep=0pt,partopsep=0pt}

% Captions centered, italic, 9.5 pt, bold label.
\captionsetup{
  font={small,it},
  labelfont={bf,it},
  justification=centering,
  singlelinecheck=false,
  skip=3pt
}
\DeclareCaptionFont{jepcscap}{\fontsize{9.5pt}{10pt}\selectfont}
\captionsetup{font={jepcscap,it}}

% Journal metadata.
\newcommand{\JEPCSVolume}{X}
\newcommand{\JEPCSNumber}{X}
\newcommand{\JEPCSYear}{2026}
\newcommand{\JEPCSArticleID}{JEPCS-2026-XXXX}
\newcommand{\JEPCSDOI}{10.xxxx/jepcs.2026.xxxx}
\newcommand{\JEPCSArticleType}{Research Article / Review Article / Short Communication / Technical Note}
\newcommand{\JEPCSReceived}{Month Day, YYYY}
\newcommand{\JEPCSAccepted}{Month Day, YYYY}
\newcommand{\JEPCSPublished}{Month Day, YYYY}
\newcommand{\JEPCSLogo}{figures/jepcs-logo.png}

\newcommand{\jepcsvolume}[1]{\renewcommand{\JEPCSVolume}{#1}}
\newcommand{\jepcsnumber}[1]{\renewcommand{\JEPCSNumber}{#1}}
\newcommand{\jepcsyear}[1]{\renewcommand{\JEPCSYear}{#1}}
\newcommand{\jepcsarticleid}[1]{\renewcommand{\JEPCSArticleID}{#1}}
\newcommand{\jepcsdoi}[1]{\renewcommand{\JEPCSDOI}{#1}}
\newcommand{\jepcsarticletype}[1]{\renewcommand{\JEPCSArticleType}{#1}}
\newcommand{\jepcsreceived}[1]{\renewcommand{\JEPCSReceived}{#1}}
\newcommand{\jepcsaccepted}[1]{\renewcommand{\JEPCSAccepted}{#1}}
\newcommand{\jepcspublished}[1]{\renewcommand{\JEPCSPublished}{#1}}
\newcommand{\jepcslogo}[1]{\renewcommand{\JEPCSLogo}{#1}}

% Author/front matter content.
\newcommand{\JEPCSTitle}{Manuscript Title: Concise, Informative, and Specific}
\newcommand{\JEPCSAuthors}{First Author\textsuperscript{1}, Second Author\textsuperscript{2}, and Last Author\textsuperscript{3,*}}
\newcommand{\JEPCSAffiliations}{%
  \textsuperscript{1} Department, Institution, City, Postal Code, Country; author1@institution.edu\\
  \textsuperscript{2} Department, Institution, City, Postal Code, Country; author2@institution.edu\\
  \textsuperscript{3} Department, Institution, City, Postal Code, Country; author3@institution.edu}
\newcommand{\JEPCSCorresponding}{corresponding.author@institution.edu}

\renewcommand{\title}[1]{\renewcommand{\JEPCSTitle}{#1}}
\renewcommand{\author}[1]{\renewcommand{\JEPCSAuthors}{#1}}
\newcommand{\jepcsaffiliations}[1]{\renewcommand{\JEPCSAffiliations}{#1}}
\newcommand{\jepcscorresponding}[1]{\renewcommand{\JEPCSCorresponding}{#1}}

% Header/footer: no header on page 1; running header from page 2 onward.
\fancypagestyle{jepcs}{%
  \fancyhf{}
  \fancyhead[C]{\fontsize{10pt}{10pt}\selectfont\itshape JEPCS | Journal of Emerging Paradigms in Computing Systems | Vol. \JEPCSVolume, No. \JEPCSNumber, \JEPCSYear}
  \renewcommand{\headrulewidth}{0.5pt}
  \fancyfoot[C]{\fontsize{8pt}{8pt}\selectfont\itshape Article ID: \JEPCSArticleID\quad |\quad DOI: \JEPCSDOI\quad |\quad Page \thepage}
  \renewcommand{\footrulewidth}{0pt}
}
\fancypagestyle{jepcsfirst}{%
  \fancyhf{}
  \renewcommand{\headrulewidth}{0pt}
  \fancyfoot[C]{\fontsize{8pt}{8pt}\selectfont Article ID: \JEPCSArticleID\quad |\quad DOI: \JEPCSDOI\quad |\quad Page \thepage}
  \renewcommand{\footrulewidth}{0pt}
}
\pagestyle{jepcs}

% Front matter block matching the Word template.
\newcommand{\makejepcstitle}{%
  \thispagestyle{jepcsfirst}%
  {\centering
    \includegraphics[width=3.79555in]{\JEPCSLogo}\par
    \vspace{2pt}
    {\bfseries\fontsize{10.5pt}{10.5pt}\selectfont Journal of Emerging Paradigms in Computing Systems (JEPCS) | Vol. \JEPCSVolume, No. \JEPCSNumber, \JEPCSYear\par}%
  }%
  \vspace{-1.5pt}\noindent\rule{\textwidth}{1.1pt}\par
  \vspace{3.5pt}
  {\centering\fontsize{10.5pt}{10.5pt}\selectfont\textbf{ARTICLE TYPE:} \JEPCSArticleType\par}
  \vspace{3.5pt}
  \noindent\begin{tabularx}{\textwidth}{@{}X X@{}}
    \textbf{Article ID:} \JEPCSArticleID & \raggedleft\arraybackslash\textbf{DOI:} \JEPCSDOI
  \end{tabularx}\par
  \vspace{8pt}
  {\centering
    {\bfseries\fontsize{16pt}{16pt}\selectfont \JEPCSTitle\par}
    \vspace{2pt}
    {\bfseries\fontsize{10.5pt}{10.5pt}\selectfont \JEPCSAuthors\par}
    \vspace{0pt}
    {\fontsize{9.5pt}{9.5pt}\selectfont \JEPCSAffiliations\par}
    \vspace{0pt}
    {\fontsize{9.5pt}{9.5pt}\selectfont * Corresponding author: \href{mailto:\JEPCSCorresponding}{\JEPCSCorresponding}\par}
  }%
  \vspace{12pt}
  {\centering\fontsize{9pt}{9pt}\selectfont
      \textbf{Received:} \JEPCSReceived\quad
      \textbf{Accepted:} \JEPCSAccepted\quad
      \textbf{Published:} \JEPCSPublished\par}
  \vspace{7pt}
}

\newcommand{\jepcsabstract}[1]{%
  \noindent\rule{\textwidth}{0.45pt}\par\vspace{4pt}
  {\fontsize{10.5pt}{11.1pt}\selectfont\justifying\textbf{\fontsize{11.5pt}{11.5pt}\selectfont Abstract-} #1\par}
  \vspace{18pt}
}

\newcommand{\jepcskeywords}[1]{%
  {\fontsize{10.5pt}{11pt}\selectfont\textbf{\fontsize{11.5pt}{11.5pt}\selectfont Keywords-} #1\par}
  \vspace{1pt}\noindent\rule{\textwidth}{0.45pt}\par
}

\newcommand{\jepcstemplatenote}[1]{%
  \vspace{4pt}
  \begin{center}\itshape\fontsize{9.5pt}{10.2pt}\selectfont #1\end{center}
  \vspace{8pt}
}

\newcommand{\jepcsinstruction}[1]{{\itshape\fontsize{9.5pt}{10pt}\selectfont #1\par}}

\newcommand{\jepcsdeclaration}[2]{\textbf{#1:} #2\par}

% References: compact 9.5 pt hanging indent.
\newenvironment{jepcsreferences}{%
  \fontsize{9.5pt}{10.1pt}\selectfont
  \setlength{\parskip}{2pt}
}{ }
\newcommand{\jepcsref}[2]{\par\hangindent=0.22in\hangafter=1\noindent[#1] #2}

% Checklist boxes; uses a plain Unicode square available in Tinos fallback.
\newcommand{\checkitem}[1]{\par\hangindent=0.18in\hangafter=1\noindent\ensuremath{\Box}\hspace{0.08in}#1}
