import plotly.graph_objects as go
from plotly.subplots import make_subplots
import pandas as pd
from ipywidgets import widgets

def __get_ohlc_chart_data(df: pd.DataFrame):

    trace = None
    if {'open', 'high', 'low', 'close'}.issubset(df.columns):

        trace = go.Candlestick(x=df.index,
                    open=df['open'], high=df['high'],
                    low=df['low'], close=df['close'], name="OHLC")

    return trace

def __get_volume_chart_data(df: pd.DataFrame):

    trace = None
    if {'volume'}.issubset(df.columns):
        trace = go.Bar(name="Volume", x=df.index, y=df['volume'], opacity=0.5, marker={"color":"rgb(7, 89, 148)"})

    return trace

def __single_symbol_plot(df: pd.DataFrame, pre_title_text):
    fig = make_subplots(specs=[[{"secondary_y": True}]])

    ohlc = __get_ohlc_chart_data(df)
    title = ""

    if ohlc:
        title = ohlc.name
        fig.add_trace(ohlc, secondary_y=False)
        fig.update_layout(xaxis_rangeslider_visible=False, xaxis_type="category", xaxis_categoryorder='category ascending')
        title=ohlc.name

    volume = __get_volume_chart_data(df)

    if volume:
        fig.add_trace(volume, secondary_y=True)
        if title == "":
            title = volume.name
        else:
            title+="V"

    fig.update_layout(title=f'{pre_title_text}{title}', title_x=0.5)
    fig.update_layout(plot_bgcolor='rgb(255, 255, 255)')
    fig.update_layout(yaxis={
          "tickcolor": "rgba(0,0,0,0.75)",
      
          "gridcolor": "rgba(0,0,0,0.25)",
      
          "zerolinecolor": "black",
        })

    return fig


def __validate_and_return_figures(plot):
    if isinstance(plot, widgets.VBox):
        figs = plot.children
    elif isinstance(plot, go.FigureWidget):
        figs = [plot]
    elif isinstance(plot, list) and isinstance(plot[0], go.FigureWidget):# list of figure widgets
        figs = plot
    else:
        raise ValueError(f"Incorrect type {type(plot)}: needs to be a FigureWidget, a list of FigureWidgets or VBox of figure widgets")

    if len(figs) == 0:
        raise ValueError("plot has no data")

    return figs

def save_plot_as_html(plot, filename="dashboard.html"):
    dashboard = open(filename, 'w')
    dashboard.write("<html><head></head><body>\n")

    figs = __validate_and_return_figures(plot)

    for fig in figs:
        inner_html = fig.to_html().split('<body>')[1].split('</body>')[0]
        dashboard.write(inner_html)


    dashboard.write("</body></html>\n")

def plot(df: pd.DataFrame, pre_title_text: str=''):
    if isinstance(df.index, pd.MultiIndex):
        unique_symbols = set(df.index.get_level_values(0))

        children = []
        #create chart per symbol
        for symbolAndFreq in unique_symbols:
            symbol = symbolAndFreq.split()[0]
            fig = __single_symbol_plot(df.loc[symbolAndFreq], f'{pre_title_text}{symbol} ')
            children.append(go.FigureWidget(fig))

        dashboard = widgets.VBox(children)

        return dashboard

    else:
        if len(pre_title_text) == 0:
            raise Exception("Set pre_title_text to ticker symbol")

        figure = go.FigureWidget(__single_symbol_plot(df, f'{pre_title_text} '))

        return figure

def add_to_plot(plot, trace, specific_graph_identifier_by_title=None):
    
    figs = __validate_and_return_figures(plot)
        
    if len(figs) == 1:
        figs[0].add_traces(trace)
    elif specific_graph_identifier_by_title:
        traceAdded = False
        titles = []
        for fig in figs:
            titles.append(fig.layout.title.text)
            if specific_graph_identifier_by_title in fig.layout.title.text:
                fig.add_traces(trace)
                traceAdded = True
                break #only apply to one graph
        if not traceAdded:
            raise ValueError(f"specific_graph_identifier_by_title={specific_graph_identifier_by_title} was not in any graph title. Options are {titles}")
    else:
        raise ValueError("specific_graph_identifier_by_title cannot be None when passing in multiple graphs")

def __get_symbol_figure(symbol, figs):
    for fig in figs:
        if symbol in fig.layout.title.text:
            return fig
    #else cannot find
def __convert_symbol_to_plotly(symbol):
    if symbol <= 1:
        return "circle"
    elif symbol == 2:
        return "square"
    elif symbol == 3:
        return "diamond"
    elif symbol == 4:
        return "triangle-up"
    elif symbol == 5:
        return "triangle-down"

def __add_symbol_specific_alpha_trace(fig, series, column, series_graph_info_list, unique_indices):
    if series_graph_info_list:
        for count, series_graph_info in enumerate(series_graph_info_list):
            name = series_graph_info['name']
            series_type = series_graph_info['seriestype']
            is_price_related = series_graph_info['ispricerelated']
            graph_index = series_graph_info['index']
            color = series_graph_info['color']
            marker_symbol = __convert_symbol_to_plotly(series_graph_info['scattermarkersymbol'])
        
            if count > 0:
                graph_name = f"{column}_{count}"
            else:
                graph_name = column

            if is_price_related:
                series = series[series > 0]

            #parse SeriesType
            graph = None
            if series_type == 0: #line
                graph = go.Scatter(x=series.index, y=series, name=graph_name,
                             line=dict(color=color, width=4))
            elif series_type == 1: #scatter
                graph = go.Scatter(x=series.index, y = series, name=graph_name, 
                                     mode='markers', marker_color=color, marker_symbol=marker_symbol, marker_size=12)
            #elif series_type == 2: #candle
            #    pass
            elif series_type == 3: #bar
                graph = go.Bar(x=series.index, y = series, name=graph_name, marker_color=color)
            #elif series_type == 4: #flag
            #    pass
            elif series_type == 5: #stacked area
                graph = go.Scatter(x=series.index, y=series, name=graph_name,
                            stackgroup='one',
                            line=dict(color=color, width=0.5))
            elif series_type == 6: #pie
                graph = go.Pie(labels=series.index, values=series, name=graph_name,
                            textfont_size=20, hoverinfo='label+percent', 
                            marker=dict(line=dict(color=color, width=2)))
            #elif series_type == 7: #treemap
            #    graph = go.Treemap(
            #                    labels = labels,
            #                    parents = parents,
            #                    values = [10, 14, 12, 10, 2, 6, 6, 1, 4],
            #                    textinfo = "label+value+percent parent+percent
            #                    entry+percent root",
            #                    root_color="lightgrey"
            #                )

            if graph:
                if is_price_related: #price indicator (put in ohlc graph)
                    if len(unique_indices) > 0: #subplots made
                        fig.add_trace(graph, row=1, col=1)
                    else:
                        fig.add_trace(graph)
                else: #otherwise assign subgraph
                    current_graph_index = list(unique_indices).index(graph_index) + 2 #starts at 2 from previously added OHLCV graph
                    fig.add_trace(graph, row=current_graph_index, col=1)
                    if fig.layout.annotations[current_graph_index - 1].text == "Plot":
                        fig.layout.annotations[current_graph_index - 1].update(text=name)
                    else:
                        fig.layout.annotations[current_graph_index - 1].update(text=f'{fig.layout.annotations[current_graph_index-1].text} & {name}')
                
                    fig.update_xaxes(row=current_graph_index, col=1, title="Time")
                    fig.update_yaxes(row=current_graph_index, col=1, title=series_graph_info['nonpricechartyaxistitle'])

def __filter_noexception(func, iter):
    result = []
    for i in iter:
        try:
            if func(i):
                result.append(i)
        except:
            pass

    if len(result) == 0:
        return None
    else:
        return result

def __add_alpha_traces(fig, symbol_specific_alpha_dataframe: pd.DataFrame, graph_info: list):
    columns = symbol_specific_alpha_dataframe.columns.to_list()
    num_of_non_price_graphs = sum(not info['ispricerelated'] for info in graph_info)
    if num_of_non_price_graphs > 0:
        unique_indices = set(map(lambda info: info['index'], filter(lambda info: not info['ispricerelated'], graph_info)))
    else:
        unique_indices = set()

    subplots_needed = len(unique_indices) + 1 #add 1 for existing fig
    if subplots_needed > 1:
        size_of_main_plot = 0.7 #main is 70 percent
        sub_size = (1 - size_of_main_plot) / subplots_needed
        heights = [sub_size] * subplots_needed
        heights[0] = size_of_main_plot 
        specs = [[{"secondary_y": False}]] * (subplots_needed - 1)
        specs.insert(0, [{"secondary_y": True}])
        subplots = make_subplots(rows=subplots_needed, cols=1, row_heights=heights,
                                    specs=specs,
                                    vertical_spacing=0.15,
                                    shared_xaxes=True,
                                    subplot_titles=["Plot"] * subplots_needed #update later
                                 )
        subplots.add_trace(fig.data[0], row=1, col=1, secondary_y=False)
        subplots.add_trace(fig.data[1], row=1, col=1, secondary_y=True)
        subplots.update_xaxes(row=1, col=1, rangeslider_visible=False, title="Time", type="category", categoryorder='category ascending')
        subplots.update_yaxes(row=1, col=1, title="Price")
        subplots.layout.yaxis2.title.text="Unit(Volume)"
        subplots.layout.annotations[0].update(text=fig.layout.title.text)
        fig = subplots

    price = symbol_specific_alpha_dataframe["price"].dropna()
    for column in columns:
        series = symbol_specific_alpha_dataframe[column].dropna()
        
        try:
            category_series_graph_info = __filter_noexception(lambda info: column in info['name'].lower() and info['name'].lower() != column, graph_info)
            exact_series_graph_info = __filter_noexception(lambda info:info['name'].lower() == column, graph_info)
            if category_series_graph_info and len(category_series_graph_info) > 1:
                for sginfo in category_series_graph_info:
                    sginfo_name = sginfo['name'].lower()
                    category_value = float(sginfo['categoryvalue'])

                    category_series_index = series[series == category_value].index
                    if len(category_series_index) > 0:
                        __add_symbol_specific_alpha_trace(fig, price.loc[category_series_index], sginfo_name, [sginfo], unique_indices)
            if exact_series_graph_info:
                __add_symbol_specific_alpha_trace(fig, series, column, exact_series_graph_info, unique_indices)
        except Exception as ex:
            print(f"Error: {ex}")

    return fig

def add_alpha_to_plot(plot, alpha_dataframe: pd.DataFrame, graph_info: list):
    figs = __validate_and_return_figures(plot)
    
    if isinstance(alpha_dataframe.index, pd.MultiIndex):
        unique_symbols = set(alpha_dataframe.index.get_level_values(0))

        children = []
        #create chart per symbol
        for symbolAndFreq in unique_symbols:
            symbol = symbolAndFreq.split()[0]
            fig = __get_symbol_figure(symbol, figs)
            if fig:
                #add traces
                fig = __add_alpha_traces(fig, alpha_dataframe.loc[symbolAndFreq], graph_info)

                fig.update_layout(height=900, width=900)

                children.append(go.FigureWidget(fig))

        plot = widgets.VBox(children)

        return plot
    else:
        raise ValueError("alpha_dataframe needs to be a multiindex data frame. Use QuantBook.AlphaModel")